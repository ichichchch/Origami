namespace Origami.Web.Services;

public sealed class ContentService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    // The protection password is hashed with the same Identity hasher as the administrator account.
    private static readonly PasswordHasher<Content> ProtectionHasher = new();
    public async Task<PagedResult<ContentSummary>> ListAdminAsync(ContentKind kind, int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default, ContentStatus? status = null)
    {

        await admin.RequireAsync(ct);

        var offset = ContentRules.Offset(page, pageSize);
        if (!Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        if (status is { } filter && !Enum.IsDefined(filter)) throw new CmsException("內容狀態無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        // Without an explicit filter the list hides the trash; it has its own tab and lifecycle.
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == kind);
        query = status is null ? query.Where(x => x.Status != ContentStatus.Trashed) : query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).Select(x => new ContentSummary(x.Id, x.Kind, x.Title, x.Slug, x.Status, x.UpdatedAt, x.PublishedAt, x.Version)
            {
                PublishAt = x.PublishAt, DeletedAt = x.DeletedAt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate,
                Views = x.Views.Sum(v => (int?)v.Count) ?? 0
            }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<ContentCounts> CountAdminAsync(ContentKind kind, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.Contents.AsNoTracking().Where(x => x.Kind == kind)
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        return new(counts.Where(x => x.Status == ContentStatus.Draft).Sum(x => x.Count),
            counts.Where(x => x.Status == ContentStatus.Published).Sum(x => x.Count),
            counts.Where(x => x.Status == ContentStatus.Trashed).Sum(x => x.Count));
    }

    public async Task<ContentEdit> GetEditAsync(Guid id, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery().Where(x => x.Id == id).Select(x => new ContentEdit
        {
            Id = x.Id, Kind = x.Kind, Title = x.Title, Slug = x.Slug, Markdown = x.Markdown, Excerpt = x.Excerpt,
            IsSticky = x.IsSticky, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate, HasProtectPassword = x.ProtectionHash != "",
            PublishAt = x.PublishAt, PreviewToken = x.PreviewToken, PublishedAt = x.PublishedAt,
            Status = x.Status, Version = x.Version,
            Views = x.Views.Sum(v => (int?)v.Count) ?? 0,
            CategoryIds = x.Categories.Select(c => c.Id).ToList(), TagIds = x.Tags.Select(t => t.Id).ToList()
        }).SingleOrDefaultAsync(ct) ?? throw new CmsException("內容不存在。");
    }

    public async Task<ContentEdit> SaveAsync(ContentEdit input, CancellationToken ct = default)
    {
        // Convenience wrapper. The kind of an existing content always comes from the stored row;
        // a new content states its kind explicitly and it is validated before use.
        return input.Id is null
            ? await CreateAsync(input.Kind, input, ct)
            : await UpdateAsync(input, ct);
    }

    public async Task<ContentEdit> CreateAsync(ContentKind kind, ContentEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (input.Id is not null) throw new CmsException("既有內容請改用更新。");
        return await SaveCoreAsync(kind, input, null, ct);
    }

    public async Task<ContentEdit> UpdateAsync(ContentEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (input.Id is not { } id) throw new CmsException("缺少內容識別。");
        return await SaveCoreAsync(null, input, id, ct);
    }

    /// <summary>
    /// The stored row decides the content kind. A caller may request a kind only when creating
    /// content, and the value is validated against the persisted type on every update.
    /// </summary>
    private async Task<ContentEdit> SaveCoreAsync(ContentKind? requestedKind, ContentEdit input, Guid? id, CancellationToken ct)
    {
        ValidateSaveInput(input, requestedKind);
        var slug = ContentRules.NormalizeSlug(id is null && string.IsNullOrWhiteSpace(input.Slug) ? input.Title : input.Slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var entity = await LoadOrCreateAsync(db, input, id, requestedKind, now, ct);
        if (entity.Kind == ContentKind.Page && (input.CategoryIds.Count != 0 || input.TagIds.Count != 0))
            throw new CmsException("獨立頁面不可指派文章分類或標籤。");
        if (entity.Kind == ContentKind.Page && input.IsSticky)
            throw new CmsException("獨立頁面不可置頂，置頂僅用於文章列表。");
        if (input.IsPrivate && input.IsProtected)
            throw new CmsException("私密與密碼保護不可同時啟用，請擇一。");
        entity.IsPrivate = input.IsPrivate;
        if (!input.IsProtected)
        {
            entity.IsProtected = false;
            entity.ProtectionHash = "";
        }
        else
        {
            var newPassword = input.NewProtectPassword ?? "";
            if (entity.ProtectionHash == "" || newPassword != "")
            {
                if (newPassword.Length < 8)
                    throw new CmsException("保護密碼至少需 8 個字元。");
                entity.ProtectionHash = ProtectionHasher.HashPassword(entity, newPassword);
            }
            entity.IsProtected = true;
        }
        input.NewProtectPassword = null;
        // A schedule is a draft with a future time; publishing always abandons the schedule.
        if (input.Status == ContentStatus.Published && input.PublishAt is not null)
            throw new CmsException("排程時間僅適用於草稿；發布前請先清除排程。");
        if (input.Status == ContentStatus.Draft && input.PublishAt is { } schedule
            && schedule <= now.AddMinutes(Limits.ScheduleLeadMinutes))
            throw new CmsException($"排程時間至少需在 {Limits.ScheduleLeadMinutes} 分鐘之後。");
        var oldSlug = entity.Slug;
        // The state being overwritten becomes a revision; it rides in the same transaction as the save.
        ContentRevision? revision = null;
        if (id is not null)
            revision = new ContentRevision { ContentId = entity.Id, Title = entity.Title, Markdown = entity.Markdown, Excerpt = entity.Excerpt, CreatedAt = now };
        var categoryIds = input.CategoryIds.Distinct().ToList();
        var tagIds = input.TagIds.Distinct().ToList();
        var categories = await db.Categories.Where(x => categoryIds.Contains(x.Id)).ToListAsync(ct);
        var tags = await db.Tags.Where(x => tagIds.Contains(x.Id)).ToListAsync(ct);
        if (categories.Count != categoryIds.Count || tags.Count != tagIds.Count) throw new CmsException("所選分類或標籤不存在，請重新載入。");
        entity.Title = input.Title.Trim();
        entity.Slug = slug;
        entity.Markdown = input.Markdown ?? "";
        entity.Excerpt = (input.Excerpt ?? "").Trim();
        entity.IsSticky = input.IsSticky;
        entity.Status = input.Status;
        if (entity.Status == ContentStatus.Published)
        {
            entity.PublishedAt ??= now;
            entity.PublishAt = null;
        }
        else
        {
            entity.PublishAt = input.Status == ContentStatus.Draft ? input.PublishAt : null;
        }
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        entity.Categories = categories;
        entity.Tags = tags;
        // A slug retired by an update to already-published content keeps serving at its old URL
        // through a permanent redirect to the content's live slug.
        if (id is not null && entity.PublishedAt is not null && oldSlug != slug)
        {
            // Another content may have owned this slug earlier; the redirect must follow the current owner.
            var existing = await db.SlugRedirects.SingleOrDefaultAsync(x => x.OldSlug == oldSlug, ct);
            if (existing is null) db.SlugRedirects.Add(new ContentSlugRedirect { OldSlug = oldSlug, ContentId = entity.Id, CreatedAt = now });
            else existing.ContentId = entity.Id;
        }
        if (revision is not null)
        {
            db.ContentRevisions.Add(revision);
            await TrimRevisionsAsync(db, entity.Id, ct);
        }
        await SyncEmbeddingAsync(db, entity, now, ct);
        // EF wraps the content update and both join-table updates in one transaction.
        await ContentRules.SaveAsync(db, ct);
        input.Id = entity.Id;
        input.Kind = entity.Kind;
        input.Slug = entity.Slug;
        input.Excerpt = entity.Excerpt;
        input.PublishAt = entity.PublishAt;
        input.PreviewToken = entity.PreviewToken;
        input.PublishedAt = entity.PublishedAt;
        input.HasProtectPassword = entity.ProtectionHash != "";
        input.Version = entity.Version;
        return input;
    }

    /// <summary>Keeps only the newest revisions per content; runs inside the save transaction.</summary>
    private static async Task TrimRevisionsAsync(CmsDbContext db, Guid contentId, CancellationToken ct)
    {
        // A tracking query also sees the revision pending in this transaction, keeping the cap exact.
        var stale = await db.ContentRevisions.Where(x => x.ContentId == contentId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(Limits.RevisionKeep).ToListAsync(ct);
        if (stale.Count > 0) db.ContentRevisions.RemoveRange(stale);
    }

    private static void ValidateSaveInput(ContentEdit input, ContentKind? requestedKind)
    {
        ContentRules.Validate(input);
        if (!Enum.IsDefined(input.Status)) throw new CmsException("內容狀態無效。");
        if (requestedKind is { } kind && !Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        if (input.CategoryIds is null || input.TagIds is null || input.CategoryIds.Count > Limits.MaxTaxonomiesPerKind || input.TagIds.Count > Limits.MaxTaxonomiesPerKind)
            throw new CmsException($"每篇文章最多 {Limits.MaxTaxonomiesPerKind} 個分類與標籤。");
    }

    private static async Task<Content> LoadOrCreateAsync(CmsDbContext db, ContentEdit input, Guid? id, ContentKind? requestedKind, DateTimeOffset now, CancellationToken ct)
    {
        if (id is not { } existingId)
        {
            var created = new Content { Kind = requestedKind!.Value, CreatedAt = now };
            db.Contents.Add(created);
            return created;
        }
        var entity = await db.Contents.Include(x => x.Categories).Include(x => x.Tags).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == existingId, ct)
            ?? throw new CmsException("內容已被刪除。");
        // The persisted kind is authoritative; a submitted change is refused, never applied.
        if (input.Kind != entity.Kind) throw new CmsException("不可變更既有內容類型。");
        if (entity.Status == ContentStatus.Trashed) throw new CmsException("回收桶內容請先還原再編輯。");
        if (entity.Version != input.Version) throw new CmsException("資料已被另一個分頁更新，請重新載入後再編輯。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        return entity;
    }

    private static async Task SyncEmbeddingAsync(CmsDbContext db, Content entity, DateTimeOffset now, CancellationToken ct)
    {
        // The embedding lifecycle rides in the same transaction as the content change:
        // a published state queues re-embedding, any other state removes vectors immediately
        // so withdrawn or draft content can never surface through semantic search. Password-
        // protected and private content never enter the index: vectors would leak gated text.
        if (entity.Status == ContentStatus.Published && !entity.IsProtected && !entity.IsPrivate)
        {
            if (!await db.EmbeddingJobs.AnyAsync(x => x.ContentId == entity.Id, ct))
                db.EmbeddingJobs.Add(new EmbeddingJob { ContentId = entity.Id, EnqueuedAt = now, NextAttemptAt = now });
        }
        else
        {
            db.EmbeddingJobs.RemoveRange(await db.EmbeddingJobs.Where(x => x.ContentId == entity.Id).ToListAsync(ct));
            db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == entity.Id).ToListAsync(ct));
        }
    }

    public async Task DeleteAsync(Guid id, Guid version, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認永久刪除。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>Moves content to the trash: public routes and search stop serving it, nothing is destroyed.</summary>
    public async Task TrashAsync(Guid id, Guid version, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認移入回收桶。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        if (entity.Status == ContentStatus.Trashed) throw new CmsException("內容已在回收桶中。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        var now = clock.GetUtcNow();
        entity.Status = ContentStatus.Trashed;
        entity.DeletedAt = now;
        entity.PublishAt = null;      // a schedule dies with the trash action
        entity.PreviewToken = null;   // preview links die immediately
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        await SyncEmbeddingAsync(db, entity, now, ct);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>Returns trashed content to the draft state for further editing.</summary>
    public async Task RestoreAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        if (entity.Status != ContentStatus.Trashed) throw new CmsException("只有回收桶內容可以還原。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        var now = clock.GetUtcNow();
        entity.Status = ContentStatus.Draft;
        entity.DeletedAt = null;
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>System operation: publishes drafts whose scheduled time arrived. Returns promoted count.</summary>
    public async Task<int> PromoteDueAsync(CancellationToken ct = default)
    {
        var promoted = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow();
            var due = await db.Contents.Where(x => x.Status == ContentStatus.Draft && x.PublishAt != null && x.PublishAt <= now)
                .OrderBy(x => x.PublishAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (due is null) break;
            // The scheduled moment is the publish moment, so ordering by publish time stays honest.
            due.PublishedAt = due.PublishAt;
            due.PublishAt = null;
            due.Status = ContentStatus.Published;
            due.UpdatedAt = now;
            due.Version = Guid.NewGuid();
            await SyncEmbeddingAsync(db, due, now, ct);
            await ContentRules.SaveAsync(db, ct);
            promoted++;
        }
        return promoted;
    }

    /// <summary>System operation: permanently deletes trashed content past the retention window.</summary>
    public async Task<int> PurgeExpiredTrashAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var cutoff = clock.GetUtcNow().AddDays(-Limits.TrashRetentionDays);
        var expired = await db.Contents.Where(x => x.Status == ContentStatus.Trashed && x.DeletedAt < cutoff)
            .Select(x => x.Id).ToListAsync(ct);
        if (expired.Count == 0) return 0;
        await db.Contents.Where(x => expired.Contains(x.Id)).ExecuteDeleteAsync(ct);
        return expired.Count;
    }

    public async Task<List<RevisionItem>> ListRevisionsAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentRevisions.AsNoTracking().Where(x => x.ContentId == contentId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(Limits.RevisionKeep)
            .Select(x => new RevisionItem(x.Id, x.Title, x.CreatedAt)).ToListAsync(ct);
    }

    public async Task<RevisionDetail?> GetRevisionAsync(Guid revisionId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentRevisions.AsNoTracking().Where(x => x.Id == revisionId)
            .Select(x => new RevisionDetail(x.Id, x.ContentId, x.Title, x.Markdown, x.Excerpt, x.CreatedAt)).SingleOrDefaultAsync(ct);
    }

    /// <summary>Replaces the share token; the previous link stops working immediately.</summary>
    public async Task<Guid> CyclePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        return (await ModifyPreviewTokenAsync(contentId, Guid.NewGuid(), ct))!.Value;
    }

    /// <summary>Removes the share token; the preview link stops working immediately.</summary>
    public async Task RevokePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await ModifyPreviewTokenAsync(contentId, null, ct);
    }

    private async Task<Guid?> ModifyPreviewTokenAsync(Guid contentId, Guid? token, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == contentId, ct) ?? throw new CmsException("內容不存在。");
        if (entity.Status == ContentStatus.Trashed) throw new CmsException("回收桶內容不可建立預覽連結。");
        var now = clock.GetUtcNow();
        entity.PreviewToken = token;
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
        return entity.PreviewToken;
    }

    /// <summary>Draft reachable through a share token; trashed and published content never render here.</summary>
    public async Task<PublicContent?> GetPreviewAsync(Guid token, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery()
            .Where(x => x.PreviewToken == token && x.Status == ContentStatus.Draft)
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => new NavigationItem(c.Name, c.Slug)).ToList(),
                x.Tags.OrderBy(t => t.Name).Select(t => new NavigationItem(t.Name, t.Slug)).ToList())
            { Excerpt = x.Excerpt }).SingleOrDefaultAsync(ct);
    }

    /// <summary>A retired token whose content is now published resolves to the public URL.</summary>
    public async Task<SlugRedirectTarget?> ResolvePublishedPreviewAsync(Guid token, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking()
            .Where(x => x.PreviewToken == token && x.Status == ContentStatus.Published)
            .Select(x => new SlugRedirectTarget(x.Slug, x.Kind)).SingleOrDefaultAsync(ct);
    }

    public async Task<PagedResult<ContentSummary>> ListPublishedAsync(int page = 1, int pageSize = Limits.PageSize,
        CancellationToken ct = default, string? category = null, string? tag = null)
    {
        var offset = ContentRules.Offset(page, pageSize);
        if (category is not null) category = ContentRules.NormalizeSlug(category);
        if (tag is not null) tag = ContentRules.NormalizeSlug(tag);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsPrivate);
        if (category is not null) query = query.Where(x => x.Categories.Any(c => c.Slug == category));
        if (tag is not null) query = query.Where(x => x.Tags.Any(t => t.Slug == tag));
        var total = await query.CountAsync(ct);
        // Sticky posts lead every page of the public list; the stable sort keys keep pagination consistent.
        var items = await query.OrderByDescending(x => x.IsSticky).ThenByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Skip(offset).Take(pageSize)
            .Select(x => new ContentSummary(x.Id, x.Kind, x.Title, x.Slug, x.Status, x.UpdatedAt, x.PublishedAt, x.Version)
            {
                Excerpt = x.IsProtected ? "此內容受密碼保護。" : x.Excerpt != "" ? x.Excerpt : x.Markdown.Substring(0, x.Markdown.Length > 600 ? 600 : x.Markdown.Length),
                ExcerptIsManual = x.IsProtected || x.Excerpt != "",
                IsSticky = x.IsSticky,
                IsProtected = x.IsProtected
            }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<List<PublicTaxonomy>> PopularCategoriesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking()
            .Select(x => new { x.Name, x.Slug, Count = x.Contents.Count(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate) })
            .Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Slug)
            .Take(Limits.MaxTaxonomiesPerKind).Select(x => new PublicTaxonomy(x.Name, x.Slug, x.Count)).ToListAsync(ct);
    }

    public async Task<NavigationItem?> GetPublicTaxonomyAsync(TaxonomyKind kind, string slug, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind)) throw new CmsException("分類類型無效。");
        slug = ContentRules.NormalizeSlug(slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        // A taxonomy used only by drafts is not part of the public site.
        return kind == TaxonomyKind.Category
            ? await db.Categories.AsNoTracking().Where(x => x.Slug == slug && x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate))
                .Select(x => new NavigationItem(x.Name, x.Slug)).SingleOrDefaultAsync(ct)
            : await db.Tags.AsNoTracking().Where(x => x.Slug == slug && x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate))
                .Select(x => new NavigationItem(x.Name, x.Slug)).SingleOrDefaultAsync(ct);
    }

    public async Task<PublicContent?> GetPublishedAsync(ContentKind kind, string slug, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery().Where(x => x.Kind == kind && x.Status == ContentStatus.Published && x.Slug == slug)
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => new NavigationItem(c.Name, c.Slug)).ToList(),
                x.Tags.OrderBy(t => t.Name).Select(t => new NavigationItem(t.Name, t.Slug)).ToList())
            { Excerpt = x.Excerpt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate }).SingleOrDefaultAsync(ct);
    }

    /// <summary>Verifies the protection password and returns the unlock token for the browser cookie.</summary>
    public async Task<string?> VerifyProtectionAsync(Guid contentId, string? password, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var stored = await db.Contents.AsNoTracking().Where(x => x.Id == contentId && x.Status == ContentStatus.Published && x.IsProtected)
            .Select(x => new { x.Id, x.ProtectionHash }).SingleOrDefaultAsync(ct);
        if (stored is null || stored.ProtectionHash == "") return null;
        if (ProtectionHasher.VerifyHashedPassword(new Content { Id = stored.Id }, stored.ProtectionHash, password ?? "") == PasswordVerificationResult.Failed)
            return null;
        return UnlockToken(stored.ProtectionHash);
    }

    /// <summary>A cookie token derived from the stored hash; changing the password invalidates old cookies.</summary>
    public async Task<bool> IsUnlockedAsync(Guid contentId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        var hash = await db.Contents.AsNoTracking().Where(x => x.Id == contentId && x.Status == ContentStatus.Published && x.IsProtected)
            .Select(x => x.ProtectionHash).SingleOrDefaultAsync(ct);
        return hash is not null && hash != "" && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(token), Convert.FromHexString(UnlockToken(hash)));
    }

    private static string UnlockToken(string protectionHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protectionHash)));

    /// <summary>Records one public view as a daily counter; failures never break rendering.</summary>
    public async Task RecordViewAsync(Guid contentId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "ContentViews" ("ContentId", "Date", "Count") VALUES ({contentId}, {date}, 1)
                ON CONFLICT ("ContentId", "Date") DO UPDATE SET "Count" = "ContentViews"."Count" + 1
                """, ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // View counting is best-effort; a failed counter must never fail a page render.
        }
    }

    /// <summary>
    /// Resolves a retired slug to the content's live published slug. A redirect never surfaces
    /// withdrawn or deleted content: those rows are removed by cascade or filtered here.
    /// </summary>
    public async Task<SlugRedirectTarget?> RedirectTargetAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var target = await db.SlugRedirects.AsNoTracking()
            .Where(x => x.OldSlug == slug && x.Content.Status == ContentStatus.Published && !x.Content.IsPrivate)
            .Select(x => new { x.Content.Slug, x.Content.Kind }).SingleOrDefaultAsync(ct);
        return target is null ? null : new SlugRedirectTarget(target.Slug, target.Kind);
    }

    /// <summary>
    /// Every public URL for the sitemap: the home page, published posts and pages, and category
    /// listings that currently have at least one published post. Withdrawn content disappears
    /// immediately because the query only considers the live published state.
    /// </summary>
    public async Task<List<SitemapItem>> SitemapAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var contents = await db.Contents.AsNoTracking()
            .Where(x => x.Status == ContentStatus.Published && !x.IsPrivate)
            .Select(x => new { x.Kind, x.Slug, x.UpdatedAt }).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking()
            .Select(x => new { x.Slug, Latest = x.Contents
                .Where(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate)
                .Max(c => (DateTimeOffset?)c.PublishedAt) })
            .Where(x => x.Latest != null).ToListAsync(ct);
        var homeLastModified = contents.Count == 0 ? null : (DateTimeOffset?)contents.Max(x => x.UpdatedAt);
        List<SitemapItem> items = [new("/", homeLastModified)];
        items.AddRange(contents.Where(x => x.Kind == ContentKind.Post).Select(x => new SitemapItem("/posts/" + x.Slug, x.UpdatedAt)));
        items.AddRange(contents.Where(x => x.Kind == ContentKind.Page).Select(x => new SitemapItem("/pages/" + x.Slug, x.UpdatedAt)));
        items.AddRange(categories.Select(x => new SitemapItem("/?category=" + x.Slug, x.Latest)));
        return items;
    }

    public async Task<List<NavigationItem>> NavigationAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Page && x.Status == ContentStatus.Published)
            .OrderBy(x => x.Title).ThenBy(x => x.Id).Select(x => new NavigationItem(x.Title, x.Slug)).ToListAsync(ct);
    }
}
