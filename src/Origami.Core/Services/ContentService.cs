namespace Origami.Core.Services;

public sealed class ContentService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    // 保護密碼與管理員帳號使用同一個 Identity 雜湊器處理。
    private static readonly PasswordHasher<Content> ProtectionHasher = new();
    public async Task<PagedResult<ContentSummary>> ListAdminAsync(ContentKind kind, int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default, ContentStatus? status = null, string? search = null)
    {

        await admin.RequireAsync(ct);

        var offset = ContentRules.Offset(page, pageSize);
        if (!Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        if (status is { } filter && !Enum.IsDefined(filter)) throw new CmsException("內容狀態無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        // 未指定狀態篩選時列表會隱藏回收桶；回收桶另有專屬分頁與生命週期。
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == kind);
        query = status is null ? query.Where(x => x.Status != ContentStatus.Trashed) : query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // 以標題與 slug 的包含比對搜尋，讓 CJK 文字的後台搜尋結果可預期。
            var pattern = "%" + ContentRules.EscapeLike(search.Trim()) + "%";
            query = query.Where(x => EF.Functions.ILike(x.Title, pattern) || EF.Functions.ILike(x.Slug, pattern));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).Select(x => new ContentSummary(x.Id, x.Kind, x.Title, x.Slug, x.Status, x.UpdatedAt, x.PublishedAt, x.Version)
            {
                PublishAt = x.PublishAt, DeletedAt = x.DeletedAt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate,
                ParentId = x.ParentId, ParentTitle = x.Parent != null ? x.Parent.Title : "",
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
            ParentId = x.ParentId,
            PublishAt = x.PublishAt, PreviewToken = x.PreviewToken, PublishedAt = x.PublishedAt,
            Status = x.Status, Version = x.Version,
            Views = x.Views.Sum(v => (int?)v.Count) ?? 0,
            CategoryIds = x.Categories.Select(c => c.Id).ToList(), TagIds = x.Tags.Select(t => t.Id).ToList()
        }).SingleOrDefaultAsync(ct) ?? throw new CmsException("內容不存在。");
    }

    public async Task<ContentEdit> SaveAsync(ContentEdit input, CancellationToken ct = default)
    {
        // 便利包裝。既有內容的類型一律來自儲存的資料列；
        // 新內容則明確宣告類型，並在使用前驗證。
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
    /// 內容類型由儲存的資料列決定。呼叫端僅能在建立內容時指定類型，
    /// 且每次更新都會對照資料庫中保存的類型驗證。
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
        entity.ParentId = input.ParentId;
        // 階層變更會鎖定頁面資料列直到儲存送出，因此兩個可能形成循環的
        // 平行儲存會被序列化，後到者讀到的是已送出的狀態，
        // 而不是過期的快照。
        IDbContextTransaction? hierarchyTransaction = null;
        if (input.ParentId is { } parentId)
        {
            hierarchyTransaction = await db.Database.BeginTransactionAsync(ct);
            await LockPageRowsAsync(db, ct);
            await ValidateParentAsync(db, entity, parentId, ct);
        }
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
        // 排程只是附帶未來時間的草稿；發布一律放棄排程。
        if (input.Status == ContentStatus.Published && input.PublishAt is not null)
            throw new CmsException("排程時間僅適用於草稿；發布前請先清除排程。");
        if (input.Status == ContentStatus.Draft && input.PublishAt is { } schedule
            && schedule <= now.AddMinutes(Limits.ScheduleLeadMinutes))
            throw new CmsException($"排程時間至少需在 {Limits.ScheduleLeadMinutes} 分鐘之後。");
        var oldSlug = entity.Slug;
        // 被覆寫的舊狀態成為修訂版本；與儲存共用同一個交易。
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
        // 已發布內容因更新而退役的 slug，仍透過永久轉址指向內容現行 slug，
        // 讓舊網址繼續可用。
        if (id is not null && entity.PublishedAt is not null && oldSlug != slug)
        {
            // 這個 slug 可能早先屬於其他內容；轉址必須跟隨目前的所有者。
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
        // EF Core 會把內容更新與兩個關聯表的更新包進同一個交易。
        await ContentRules.SaveAsync(db, ct);
        if (hierarchyTransaction is not null) await hierarchyTransaction.CommitAsync(ct);
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

    /// <summary>每份內容僅保留最新的修訂版本；在儲存交易內執行。</summary>
    private static async Task TrimRevisionsAsync(CmsDbContext db, Guid contentId, CancellationToken ct)
    {
        // 追蹤查詢也看得到本交易中待寫入的修訂版本，讓保留上限精準。
        var stale = await db.ContentRevisions.Where(x => x.ContentId == contentId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(Limits.RevisionKeep).ToListAsync(ct);
        if (stale.Count > 0) db.ContentRevisions.RemoveRange(stale);
    }

    /// <summary>
    /// 頁面階層規則：僅獨立頁面可巢狀、上層必須是頁面、頁面不可成為自己的祖先。
    /// 祖先走訪以單次小查詢在記憶體內完成，
    /// 因此惡意鏈結無法讓伺服器陷入迴圈。
    /// </summary>
    private static async Task ValidateParentAsync(CmsDbContext db, Content entity, Guid parentId, CancellationToken ct)
    {
        if (entity.Kind != ContentKind.Page)
            throw new CmsException("僅獨立頁面可設定上層頁面。");
        if (parentId == entity.Id)
            throw new CmsException("頁面不可設為自己的上層。");
        var pages = await db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Page)
            .Select(x => new { x.Id, x.ParentId }).ToListAsync(ct);
        var byId = pages.ToDictionary(x => x.Id);
        if (!byId.ContainsKey(parentId))
            throw new CmsException("上層頁面不存在，請重新選擇。");
        // 從候選上層往上走訪；若再次遇到儲存中的頁面，代表連結形成循環。
        var guard = 0;
        Guid? cursor = parentId;
        while (cursor is not null)
        {
            if (cursor == entity.Id)
                throw new CmsException("不可將頁面設為其子孫的下層，這會形成循環階層。");
            if (++guard > 1000)
                throw new CmsException("頁面階層過深，請檢查上層設定。");
            cursor = byId.TryGetValue(cursor.Value, out var node) ? node.ParentId : null;
        }
    }

    /// <summary>以穩定順序鎖定頁面資料列，確保階層變更不會重疊。</summary>
    private static async Task LockPageRowsAsync(CmsDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Contents" WHERE "Kind" = {(int)ContentKind.Page} ORDER BY "Id" FOR UPDATE""", ct);
    }

    /// <summary>編輯器上層選擇器的頁面候選；回收桶內容不可選。</summary>
    public async Task<List<PageOption>> PageOptionsAsync(CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Page && x.Status != ContentStatus.Trashed)
            .OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Select(x => new PageOption(x.Id, x.Title, x.ParentId, x.Status, x.IsPrivate, x.IsProtected)).ToListAsync(ct);
    }

    /// <summary>渲染為固定首頁的頁面；任何無法公開閱讀的內容回傳 null。</summary>
    public async Task<PublicContent?> GetPublishedPageAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking()
            .Where(x => x.Id == id && x.Kind == ContentKind.Page && x.Status == ContentStatus.Published && !x.IsPrivate && !x.IsProtected)
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => new NavigationItem(c.Name, c.Slug)).ToList(),
                x.Tags.OrderBy(t => t.Name).Select(t => new NavigationItem(t.Name, t.Slug)).ToList())
            { Excerpt = x.Excerpt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate }).SingleOrDefaultAsync(ct);
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
        // 資料庫保存的類型才是權威；送出的類型變更會被拒絕，絕不套用。
        if (input.Kind != entity.Kind) throw new CmsException("不可變更既有內容類型。");
        if (entity.Status == ContentStatus.Trashed) throw new CmsException("回收桶內容請先還原再編輯。");
        if (entity.Version != input.Version) throw new CmsException("資料已被另一個分頁更新，請重新載入後再編輯。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        return entity;
    }

    private static async Task SyncEmbeddingAsync(CmsDbContext db, Content entity, DateTimeOffset now, CancellationToken ct)
    {
        // 嵌入生命週期與內容變更共用同一個交易：
        // 發布狀態會排入重新嵌入工作，其他狀態則立即移除向量，
        // 讓撤回或草稿內容無法經語意搜尋浮出。密碼保護與私密內容
        // 永不進入索引：向量會外洩受門檻保護的文字。
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

    /// <summary>把內容移入回收桶：公開路由與搜尋即停止提供，但不銷毀任何資料。</summary>
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
        entity.PublishAt = null;      // 排程隨移入回收桶而失效
        entity.PreviewToken = null;   // 預覽連結立即失效
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        await SyncEmbeddingAsync(db, entity, now, ct);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>把回收桶內容還原為草稿狀態以便繼續編輯。</summary>
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

    /// <summary>系統操作：發布排程時間已到的草稿。回傳發布數量。</summary>
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
            // 排程時刻即發布時刻，因此依發布時間排序保持真實。
            // 撤回後再次發布仍保留首次發布日期。
            due.PublishedAt ??= due.PublishAt;
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

    /// <summary>系統操作：永久刪除超過保留期限的回收桶內容。</summary>
    public async Task<int> PurgeExpiredTrashAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var cutoff = clock.GetUtcNow().AddDays(-Limits.TrashRetentionDays);
        var expired = await db.Contents.Where(x => x.Status == ContentStatus.Trashed && x.DeletedAt < cutoff)
            .Select(x => x.Id).ToListAsync(ct);
        if (expired.Count == 0) return 0;
        // 刪除時重複回收桶條件：列出到刪除之間被還原的內容
        // 已回到草稿狀態，必須在清理中存活。
        return await db.Contents.Where(x => expired.Contains(x.Id)
                && x.Status == ContentStatus.Trashed && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
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

    /// <summary>替換分享 token；舊連結立即失效。</summary>
    public async Task<Guid> CyclePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        return (await ModifyPreviewTokenAsync(contentId, Guid.NewGuid(), ct))!.Value;
    }

    /// <summary>移除分享 token；預覽連結立即失效。</summary>
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
        // 分享 token 不屬於被編輯的內容，因此並發版本保持不變；
        // 平行的內容儲存仍必須以衝突收場，不得靜默覆寫。
        await ContentRules.SaveAsync(db, ct);
        return entity.PreviewToken;
    }

    /// <summary>透過分享 token 可讀取的草稿；回收桶與已發布內容不會在此渲染。</summary>
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

    /// <summary>內容已發布時，退役 token 會解析到公開網址。</summary>
    public async Task<SlugRedirectTarget?> ResolvePublishedPreviewAsync(Guid token, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking()
            .Where(x => x.PreviewToken == token && x.Status == ContentStatus.Published)
            .Select(x => new SlugRedirectTarget(x.Slug, x.Kind)).SingleOrDefaultAsync(ct);
    }

    public async Task<PagedResult<ContentSummary>> ListPublishedAsync(int page = 1, int pageSize = Limits.PageSize,
        CancellationToken ct = default, string? category = null, string? tag = null, int? year = null, int? month = null)
    {
        var offset = ContentRules.Offset(page, pageSize);
        if (category is not null) category = ContentRules.NormalizeSlug(category);
        if (tag is not null) tag = ContentRules.NormalizeSlug(tag);
        if (month is { } m && m is < 1 or > 12) throw new CmsException("月份無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsPrivate);
        if (category is not null) query = query.Where(x => x.Categories.Any(c => c.Slug == category));
        if (tag is not null) query = query.Where(x => x.Tags.Any(t => t.Slug == tag));
        if (year is { } filterYear) query = query.Where(x => x.PublishedAt!.Value.Year == filterYear);
        if (month is { } filterMonth) query = query.Where(x => x.PublishedAt!.Value.Month == filterMonth);
        var total = await query.CountAsync(ct);
        // 置頂文章排在公開列表每一頁前頭；穩定的排序鍵讓分頁結果一致。
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

    public async Task<TaxonomyView?> GetPublicTaxonomyAsync(TaxonomyKind kind, string slug, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind)) throw new CmsException("分類類型無效。");
        slug = ContentRules.NormalizeSlug(slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        // 僅被草稿使用的分類或標籤不屬於公開網站。
        return kind == TaxonomyKind.Category
            ? await db.Categories.AsNoTracking().Where(x => x.Slug == slug && x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate))
                .Select(x => new TaxonomyView(x.Name, x.Slug, x.Description)).SingleOrDefaultAsync(ct)
            : await db.Tags.AsNoTracking().Where(x => x.Slug == slug && x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate))
                .Select(x => new TaxonomyView(x.Name, x.Slug, x.Description)).SingleOrDefaultAsync(ct);
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

    /// <summary>驗證保護密碼並回傳瀏覽器 cookie 所用的解鎖 token。</summary>
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

    /// <summary>由儲存雜湊衍生的 cookie token；變更密碼會使舊 cookie 失效。</summary>
    public async Task<bool> IsUnlockedAsync(Guid contentId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        var hash = await db.Contents.AsNoTracking().Where(x => x.Id == contentId && x.Status == ContentStatus.Published && x.IsProtected)
            .Select(x => x.ProtectionHash).SingleOrDefaultAsync(ct);
        if (hash is null || hash == "") return false;
        // 格式錯誤或來路不明的 cookie 視為未解鎖請求，絕不引發伺服器錯誤。
        if (!TryDecodeToken(token, out var presented) || !TryDecodeToken(UnlockToken(hash), out var expected)) return false;
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static bool TryDecodeToken(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length != 64) return false; // SHA-256 摘要的十六進位表示
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static string UnlockToken(string protectionHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protectionHash)));

    /// <summary>以每日計數器記錄一次公開瀏覽；失敗絕不中斷渲染。</summary>
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
            // 瀏覽計數採盡力而為；計數失敗絕不能讓頁面渲染失敗。
        }
    }

    /// <summary>
    /// 把退役 slug 解析到內容現行的已發布 slug。轉址絕不會讓撤回或已刪除的
    /// 內容浮出：這些資料列已隨串聯刪除移除，或在此被過濾掉。
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
    /// sitemap 的所有公開網址：首頁、已發布文章與頁面，
    /// 以及目前至少有一篇已發布文章的分類列表。撤回的內容會立即消失，
    /// 因為查詢只考慮現行的已發布狀態。
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
        List<SitemapItem> items = [new("/", homeLastModified), new("/archives", homeLastModified)];
        items.AddRange(contents.Where(x => x.Kind == ContentKind.Post).Select(x => new SitemapItem("/posts/" + x.Slug, x.UpdatedAt)));
        items.AddRange(contents.Where(x => x.Kind == ContentKind.Page).Select(x => new SitemapItem("/pages/" + x.Slug, x.UpdatedAt)));
        items.AddRange(categories.Select(x => new SitemapItem("/?category=" + x.Slug, x.Latest)));
        return items;
    }

    public async Task<List<PageNavItem>> NavigationAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Page && x.Status == ContentStatus.Published && !x.IsPrivate)
            .OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Select(x => new PageNavItem(x.Id, x.Title, x.Slug, x.ParentId)).ToListAsync(ct);
    }

    /// <summary>
    /// 公開時間軸上相鄰的兩篇文章，嚴格依發布時間再依 Id 排序。
    /// 私密文章不參與走訪：相鄰連結不得對訪客洩漏資訊或形成死路。
    /// </summary>
    public async Task<List<ContentNeighbor>> NeighborsAsync(Guid contentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var current = await db.Contents.AsNoTracking().Where(x => x.Id == contentId)
            .Select(x => new { x.PublishedAt, x.Id }).SingleOrDefaultAsync(ct);
        if (current is null || current.PublishedAt is not { } publishedAt) return [];
        var newer = await db.Contents.AsNoTracking()
            .Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsPrivate
                && (x.PublishedAt! > publishedAt || (x.PublishedAt == publishedAt && x.Id > current.Id)))
            .OrderBy(x => x.PublishedAt).ThenBy(x => x.Id)
            .Select(x => new ContentNeighbor(x.Id, x.Slug, x.Title, true)).FirstOrDefaultAsync(ct);
        var older = await db.Contents.AsNoTracking()
            .Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsPrivate
                && (x.PublishedAt! < publishedAt || (x.PublishedAt == publishedAt && x.Id < current.Id)))
            .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Select(x => new ContentNeighbor(x.Id, x.Slug, x.Title, false)).FirstOrDefaultAsync(ct);
        List<ContentNeighbor> neighbors = [];
        if (newer is not null) neighbors.Add(newer);
        if (older is not null) neighbors.Add(older);
        return neighbors;
    }

    /// <summary>公開彙整的各月份與文章數，最新月份在前。</summary>
    public async Task<List<ArchiveMonth>> ArchiveMonthsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // 只投影日期讓傳輸量極小；分組在記憶體內進行，供應商的日期
        // 部分轉譯便無法悄悄改變分組語意。
        var dates = await db.Contents.AsNoTracking()
            .Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published && !x.IsPrivate && x.PublishedAt != null)
            .Select(x => x.PublishedAt!.Value).ToListAsync(ct);
        return dates.GroupBy(x => (x.Year, x.Month))
            .Select(g => new ArchiveMonth(g.Key.Year, g.Key.Month, g.Count()))
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .ToList();
    }
}
