namespace Origami.Core.Services;

public sealed class ContentService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    // 保護密碼與管理員帳號使用同一個 Identity 雜湊器處理。
    private static readonly PasswordHasher<Content> ProtectionHasher = new();
    public async Task<PagedResult<ContentSummary>> ListAdminAsync(ContentKind kind, int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default, ContentStatus? status = null, string? search = null)
    {

        await admin.RequireRoleAsync(SiteRole.Reader, ct);

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
                CoverFileName = x.Cover != null ? x.Cover.FileName : "", CoverAltText = x.Cover != null ? x.Cover.AltText : "",
                AuthorName = x.AuthorName, AuthorId = x.AuthorId,
                Views = x.Views.Sum(v => (int?)v.Count) ?? 0
            }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<ContentCounts> CountAdminAsync(ContentKind kind, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        if (!Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.Contents.AsNoTracking().Where(x => x.Kind == kind)
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        return new(counts.Where(x => x.Status == ContentStatus.Draft).Sum(x => x.Count),
            counts.Where(x => x.Status == ContentStatus.Published).Sum(x => x.Count),
            counts.Where(x => x.Status == ContentStatus.Pending).Sum(x => x.Count),
            counts.Where(x => x.Status == ContentStatus.Trashed).Sum(x => x.Count));
    }

    public async Task<ContentEdit> GetEditAsync(Guid id, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        // 占用判定以伺服器時間為準；讀取者與編輯者都會看到「誰正在編輯」的提示。
        var access = await admin.GetCurrentAccessAsync(ct);
        var activeBefore = clock.GetUtcNow().AddSeconds(-Limits.EditingLockSeconds);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery().Where(x => x.Id == id).Select(x => new ContentEdit
        {
            Id = x.Id, Kind = x.Kind, Title = x.Title, Slug = x.Slug, Markdown = x.Markdown, Excerpt = x.Excerpt,
            IsSticky = x.IsSticky, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate, HasProtectPassword = x.ProtectionHash != "",
            ParentId = x.ParentId, CoverMediaId = x.CoverMediaId,
            CoverFileName = x.Cover != null ? x.Cover.FileName : "",
            CoverAltText = x.Cover != null ? x.Cover.AltText : "",
            CoverCaption = x.Cover != null ? x.Cover.Caption : "",
            PublishAt = x.PublishAt, PreviewToken = x.PreviewToken, PublishedAt = x.PublishedAt,
            Status = x.Status, Version = x.Version,
            MetaDescription = x.MetaDescription, NoIndex = x.NoIndex,
            AuthorName = x.AuthorName, AuthorId = x.AuthorId,
            EditingByOther = x.EditingById != null && x.EditingById != access.UserId && x.EditingAt > activeBefore,
            EditingByName = x.EditingByName,
            Views = x.Views.Sum(v => (int?)v.Count) ?? 0,
            ContentTypeId = x.ContentTypeId,
            CategoryIds = x.Categories.Select(c => c.Id).ToList(), TagIds = x.Tags.Select(t => t.Id).ToList(),
            CustomTermIds = x.CustomTerms.Select(t => t.TermId).ToList(),
            FieldValues = x.FieldValues.Select(v => new FieldValueInput(v.FieldId, v.Value)).ToList()
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
        // 作者（含編輯者以上）都能建立內容；作者可用的狀態在儲存核心另有限制。
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        if (input.Id is not null) throw new CmsException("既有內容請改用更新。");
        return await SaveCoreAsync(kind, input, null, ct);
    }

    public async Task<ContentEdit> UpdateAsync(ContentEdit input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Author, ct);
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
        // 作者只能編輯本人未排程的草稿／待審；發布與排程仍由編輯者核准。
        var access = await admin.GetCurrentAccessAsync(ct);
        if (!access.CanEdit && access.Role != SiteRole.Author) throw new AccessDeniedException();
        var isAuthorOnly = !access.CanEdit;
        if (id is null)
        {
            entity.AuthorId = access.UserId;
            entity.AuthorName = access.DisplayName;
            entity.AuthorSlug = access.DisplayName.Length == 0 ? "" : ContentRules.NormalizeSlug(access.DisplayName);
            entity.ContentTypeId = input.ContentTypeId;
        }
        else
        {
            RequireAuthorEditable(entity, access);
        }
        if (isAuthorOnly && input.Status is not (ContentStatus.Draft or ContentStatus.Pending))
            throw new CmsException("作者只能儲存草稿或提交待審；發布由編輯者審核後執行。");
        if (isAuthorOnly && input.PublishAt is not null)
            throw new CmsException("作者不可設定排程；排程發布須由編輯者核准。");
        // 自訂內容類型：建立時指定，既有內容不可變更類型；封存的類型不再接受新內容。
        ContentType? contentType = null;
        if (input.ContentTypeId is { } typeId)
        {
            contentType = await db.ContentTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == typeId, ct)
                ?? throw new CmsException("內容類型不存在。");
            if (id is null && contentType.IsArchived) throw new CmsException("此內容類型已封存，不再接受新內容。");
            if (id is not null && entity.ContentTypeId != typeId) throw new CmsException("不可變更既有內容的類型。");
            if (entity.Kind != ContentKind.Post) throw new CmsException("自訂內容類型僅適用於文章形式的內容。");
        }
        else if (id is not null && entity.ContentTypeId is not null)
        {
            throw new CmsException("不可變更既有內容的類型。");
        }
        if (contentType is not null)
        {
            if (!contentType.SupportsCategories && input.CategoryIds.Count != 0)
                throw new CmsException("此內容類型不可指派分類。");
            if (!contentType.SupportsTags && input.TagIds.Count != 0)
                throw new CmsException("此內容類型不可指派標籤。");
        }
        // 階層只適用於頁面或支援階層的自訂類型；父層必須同類型。
        if (input.ParentId is { } parentContentId)
        {
            var parentKind = await db.Contents.AsNoTracking().Where(x => x.Id == parentContentId)
                .Select(x => new { x.Kind, x.ContentTypeId }).SingleOrDefaultAsync(ct)
                ?? throw new CmsException("上層內容不存在。");
            var hierarchical = entity.Kind == ContentKind.Page || (contentType?.SupportsHierarchy ?? false);
            if (!hierarchical) throw new CmsException("此內容類型不可指定上層內容。");
            if (contentType is not null && parentKind.ContentTypeId != contentType.Id)
                throw new CmsException("上層內容必須屬於同一內容類型。");
        }
        if (entity.Kind == ContentKind.Page && (input.CategoryIds.Count != 0 || input.TagIds.Count != 0))
            throw new CmsException("獨立頁面不可指派文章分類或標籤。");
        if (entity.Kind == ContentKind.Page && input.IsSticky)
            throw new CmsException("獨立頁面不可置頂，置頂僅用於文章列表。");
        if (input.IsPrivate && input.IsProtected)
            throw new CmsException("私密與密碼保護不可同時啟用，請擇一。");
        entity.IsPrivate = input.IsPrivate;
        entity.ParentId = input.ParentId;
        // 封面必須是媒體庫中現存的圖片；一次查詢同時完成驗證與回填素材。
        var cover = input.CoverMediaId is { } coverId
            ? await db.MediaAssets.AsNoTracking().Where(x => x.Id == coverId)
                .Select(x => new { x.FileName, x.AltText, x.Caption }).SingleOrDefaultAsync(ct)
            : null;
        if (input.CoverMediaId is not null && cover is null)
            throw new CmsException("封面圖片不存在，請重新選擇。");
        entity.CoverMediaId = input.CoverMediaId;
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
        // 自訂詞彙只接受屬於本內容類型之分類法的詞彙；內建文章與頁面沒有自訂詞彙。
        var termIds = input.CustomTermIds.Distinct().ToList();
        if (termIds.Count > 0)
        {
            if (contentType is null) throw new CmsException("內建文章與頁面不可指派自訂詞彙。");
            var allowed = await db.CustomTaxonomyTerms.AsNoTracking()
                .Where(term => termIds.Contains(term.Id)
                    && db.CustomTaxonomies.Any(tax => tax.Id == term.TaxonomyId && tax.ContentTypeId == contentType.Id))
                .Select(term => term.Id).ToListAsync(ct);
            if (allowed.Count != termIds.Count) throw new CmsException("所選詞彙不存在或不屬於本內容類型。");
        }
        // 自訂欄位值：欄位必須屬於本內容類型，值依欄位種類驗證；必填欄位不可留空。
        var fieldValues = input.FieldValues
            .GroupBy(x => x.FieldId)
            .Select(group => group.First())
            .ToList();
        if (fieldValues.Count > 0 || contentType is not null)
        {
            if (contentType is null) throw new CmsException("內建文章與頁面不可使用自訂欄位。");
            var fields = await db.CustomFields.AsNoTracking()
                .Where(field => field.ContentTypeId == contentType.Id)
                .Select(field => new { field.Id, field.FieldType, field.IsRequired })
                .ToListAsync(ct);
            var supplied = fieldValues.Select(x => x.FieldId).ToHashSet();
            if (supplied.Except(fields.Select(x => x.Id)).Any())
                throw new CmsException("所選自訂欄位不存在或不屬於本內容類型。");
            foreach (var value in fieldValues)
            {
                var field = fields.First(x => x.Id == value.FieldId);
                CustomFieldRules.ValidateValue(field.FieldType, value.Value, field.IsRequired);
            }
            // 必填欄位若未送出值，等同留空，必須拒絕。
            foreach (var field in fields.Where(x => x.IsRequired && !supplied.Contains(x.Id)))
                CustomFieldRules.ValidateValue(field.FieldType, "", required: true);
        }
        var currentValues = await db.ContentFieldValues.Where(x => x.ContentId == entity.Id).ToListAsync(ct);
        db.ContentFieldValues.RemoveRange(currentValues);
        foreach (var value in fieldValues)
            db.ContentFieldValues.Add(new ContentFieldValue
            {
                Id = Guid.NewGuid(), ContentId = entity.Id, FieldId = value.FieldId, Value = value.Value.Trim()
            });
        var currentTerms = await db.ContentCustomTerms.Where(x => x.ContentId == entity.Id).ToListAsync(ct);
        db.ContentCustomTerms.RemoveRange(currentTerms);
        foreach (var termId in termIds)
            db.ContentCustomTerms.Add(new ContentCustomTerm { Id = Guid.NewGuid(), ContentId = entity.Id, TermId = termId });
        entity.Title = input.Title.Trim();
        entity.Slug = slug;
        entity.Markdown = input.Markdown ?? "";
        entity.Excerpt = (input.Excerpt ?? "").Trim();
        entity.IsSticky = input.IsSticky;
        entity.MetaDescription = (input.MetaDescription ?? "").Trim();
        entity.NoIndex = input.NoIndex;
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
        input.CoverFileName = cover?.FileName ?? "";
        input.CoverAltText = cover?.AltText ?? "";
        input.CoverCaption = cover?.Caption ?? "";
        input.Version = entity.Version;
        return input;
    }

    /// <summary>
    /// 列表快速編輯：只更新標題、網址代稱與狀態，其餘欄位
    /// 一律不動。規則與完整儲存一致：slug 變更時退役舊網址為永久轉址、
    /// 發布放棄排程、狀態變更同步嵌入生命週期，並留下修訂版本。
    /// </summary>
    public async Task<QuickEditResult> QuickEditAsync(Guid id, string? title, string? slug, ContentStatus status,
        Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (status is not (ContentStatus.Draft or ContentStatus.Published or ContentStatus.Pending))
            throw new CmsException("快速編輯僅接受草稿、待審或已發布狀態；回收桶請使用還原流程。");
        var cleanTitle = (title ?? "").Trim();
        if (cleanTitle.Length is 0 or > Limits.Title) throw new CmsException($"標題須為 1–{Limits.Title} 字。");
        var normalized = ContentRules.NormalizeSlug(slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        if (entity.Status == ContentStatus.Trashed) throw new CmsException("回收桶內容請先還原再編輯。");
        if (entity.Version != version) throw new CmsException("資料已被另一個分頁更新，請重新載入後再編輯。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        var oldSlug = entity.Slug;
        var revision = new ContentRevision { ContentId = entity.Id, Title = entity.Title, Markdown = entity.Markdown, Excerpt = entity.Excerpt, CreatedAt = now };
        entity.Title = cleanTitle;
        entity.Slug = normalized;
        entity.Status = status;
        if (status == ContentStatus.Published)
        {
            entity.PublishedAt ??= now;
            entity.PublishAt = null;
        }
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        if (entity.PublishedAt is not null && oldSlug != normalized)
        {
            var existing = await db.SlugRedirects.SingleOrDefaultAsync(x => x.OldSlug == oldSlug, ct);
            if (existing is null) db.SlugRedirects.Add(new ContentSlugRedirect { OldSlug = oldSlug, ContentId = entity.Id, CreatedAt = now });
            else existing.ContentId = entity.Id;
        }
        db.ContentRevisions.Add(revision);
        await TrimRevisionsAsync(db, entity.Id, ct);
        await SyncEmbeddingAsync(db, entity, now, ct);
        await ContentRules.SaveAsync(db, ct);
        return new(entity.Id, entity.Title, entity.Slug, entity.Status, entity.Version);
    }

    public sealed record QuickEditResult(Guid Id, string Title, string Slug, ContentStatus Status, Guid Version);

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
            $"""SELECT "Id" FROM "Contents" WHERE "SiteId" = {db.CurrentSiteId} AND "Kind" = {(int)ContentKind.Page} ORDER BY "Id" FOR UPDATE""", ct);
    }

    /// <summary>編輯器上層選擇器的頁面候選；回收桶內容不可選。</summary>
    public async Task<List<PageOption>> PageOptionsAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
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
            { Excerpt = x.Excerpt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate,
                MetaDescription = x.MetaDescription, NoIndex = x.NoIndex, UpdatedAt = x.UpdatedAt,
                AuthorName = x.AuthorName, AuthorSlug = x.AuthorSlug,
                CoverFileName = x.Cover != null ? x.Cover.FileName : "",
                CoverAltText = x.Cover != null ? x.Cover.AltText : "",
                CoverCaption = x.Cover != null ? x.Cover.Caption : "",
                Fields = x.FieldValues.OrderBy(v => v.Field.Position).ThenBy(v => v.Field.Key)
                    .Select(v => new PublicFieldValue(v.Field.Name, v.Field.Key, v.Value, v.Field.FieldType)).ToList() })
            .SingleOrDefaultAsync(ct);
    }

    private static void ValidateSaveInput(ContentEdit input, ContentKind? requestedKind)
    {
        ContentRules.Validate(input);
        if (!Enum.IsDefined(input.Status)) throw new CmsException("內容狀態無效。");
        if (requestedKind is { } kind && !Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        // 回收桶有專屬的刪除／還原流程；直接以儲存進出回收桶會破壞 DeletedAt 約束。
        if (input.Status == ContentStatus.Trashed) throw new CmsException("回收桶內容請先還原再編輯，不能以儲存直接設定。");
        if (input.CategoryIds is null || input.TagIds is null || input.CategoryIds.Count > Limits.MaxTaxonomiesPerKind || input.TagIds.Count > Limits.MaxTaxonomiesPerKind)
            throw new CmsException($"每篇文章最多 {Limits.MaxTaxonomiesPerKind} 個分類與標籤。");
    }

    /// <summary>編輯占用鎖的現況回報；占用僅供提示，不阻止開啟或儲存（並發由版本檢查把關）。</summary>
    public sealed record EditingLockState(string? UserId, string DisplayName, DateTimeOffset? At, bool HeldByOther);

    /// <summary>
    /// 編輯器心跳：刷新或取得占用。占用逾期時可直接取得；他人占用中除非明確接管，
    /// 否則不改動並回報現況。心跳不參與並發控制，與內容版本檢查互不影響。
    /// </summary>
    public async Task<EditingLockState> TouchEditingAsync(Guid id, bool takeOver = false, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        var now = clock.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        RequireAuthorEditable(entity, access);
        var activeBefore = now.AddSeconds(-Limits.EditingLockSeconds);
        var heldByOther = entity.EditingById is not null && entity.EditingById != access.UserId
            && entity.EditingAt is { } at && at > activeBefore;
        if (heldByOther && !takeOver)
            return new(entity.EditingById, entity.EditingByName, entity.EditingAt, HeldByOther: true);
        var changed = await db.Contents.Where(x => x.Id == id && x.Version == entity.Version
                && x.EditingById == entity.EditingById && x.EditingByName == entity.EditingByName && x.EditingAt == entity.EditingAt
                && (access.CanEdit || (x.AuthorId == access.UserId
                    && (x.Status == ContentStatus.Draft || x.Status == ContentStatus.Pending) && x.PublishAt == null))
                && (takeOver || x.EditingById == null || x.EditingById == access.UserId || x.EditingAt == null || x.EditingAt <= activeBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EditingById, access.UserId)
                .SetProperty(x => x.EditingByName, access.DisplayName).SetProperty(x => x.EditingAt, now), ct);
        if (changed == 1)
            return new(access.UserId, access.DisplayName, now, HeldByOther: false);
        entity = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        RequireAuthorEditable(entity, access);
        heldByOther = entity.EditingById is not null && entity.EditingById != access.UserId
            && entity.EditingAt > clock.GetUtcNow().AddSeconds(-Limits.EditingLockSeconds);
        return new(entity.EditingById, entity.EditingByName, entity.EditingAt, heldByOther);
    }

    /// <summary>離開編輯器時釋放自己的占用；他人占用一律不動。</summary>
    public async Task ReleaseEditingAsync(Guid id, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        var access = await admin.GetCurrentAccessAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return;
        RequireAuthorEditable(entity, access, releasing: true);
        if (entity.EditingById != access.UserId) return;
        var changed = await db.Contents.Where(x => x.Id == id && x.Version == entity.Version
                && (access.CanEdit || x.AuthorId == access.UserId)
                && x.EditingById == access.UserId && x.EditingByName == entity.EditingByName && x.EditingAt == entity.EditingAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EditingById, (string?)null)
                .SetProperty(x => x.EditingByName, "").SetProperty(x => x.EditingAt, (DateTimeOffset?)null), ct);
        if (changed == 0)
        {
            entity = await db.Contents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (entity is not null) RequireAuthorEditable(entity, access, releasing: true);
        }
    }

    private static void RequireAuthorEditable(Content entity, SiteAccessInfo access, bool releasing = false)
    {
        if (entity.SiteId != access.SiteId) throw new AccessDeniedException();
        if (access.CanEdit) return;
        if (access.Role != SiteRole.Author || entity.AuthorId != access.UserId)
            throw new AccessDeniedException("作者只能編輯自己的內容。");
        if (!releasing && (entity.Status is not (ContentStatus.Draft or ContentStatus.Pending) || entity.PublishAt is not null))
            throw new AccessDeniedException("作者只能編輯未排程的草稿或待審內容。");
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
        await DeleteCoreAsync(id, version, ct);
    }

    /// <summary>永久刪除的核心流程；授權由呼叫端（單筆端點或批次）先行完成。</summary>
    private async Task DeleteCoreAsync(Guid id, Guid version, CancellationToken ct)
    {
        await SiteAdministrationService.WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // 與頁面階層儲存維持相同鎖序；先鎖定，再處理 Restrict 外鍵。
            await LockPageRowsAsync(db, ct);
            var entity = await db.Contents.FromSqlInterpolated($"""
                SELECT * FROM "Contents" WHERE "SiteId" = {db.CurrentSiteId} AND "Id" = {id} FOR UPDATE
                """).SingleOrDefaultAsync(ct) ?? throw new CmsException("內容不存在。");
            if (entity.Version != version)
                throw new CmsException("資料已被另一個分頁更新或刪除，請重新載入後再編輯。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            await ClearContentReferencesAsync(db, [id], clock.GetUtcNow(), ct);
            db.Remove(entity);
            await ContentRules.SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    private static async Task ClearContentReferencesAsync(CmsDbContext db, Guid[] ids, DateTimeOffset now, CancellationToken ct)
    {
        // 全域站點 filter 仍套用於 ExecuteUpdate；任何後續失敗都由外層交易回滾。
        await db.Contents.Where(x => x.ParentId != null && ids.Contains(x.ParentId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ParentId, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, Guid.NewGuid()), ct);
        await db.Settings.Where(x => x.HomePageContentId != null && ids.Contains(x.HomePageContentId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.HomePageContentId, (Guid?)null)
                .SetProperty(x => x.Version, Guid.NewGuid()), ct);
    }

    /// <summary>把內容移入回收桶：公開路由與搜尋即停止提供，但不銷毀任何資料。</summary>
    public async Task TrashAsync(Guid id, Guid version, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認移入回收桶。");
        await TrashCoreAsync(id, version, ct);
    }

    /// <summary>移入回收桶的核心流程；授權由呼叫端（單筆端點或批次）先行完成。</summary>
    private async Task TrashCoreAsync(Guid id, Guid version, CancellationToken ct)
    {
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
        await RestoreCoreAsync(id, version, ct);
    }

    /// <summary>還原的核心流程；授權由呼叫端（單筆端點或批次）先行完成。</summary>
    private async Task RestoreCoreAsync(Guid id, Guid version, CancellationToken ct)
    {
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

    /// <summary>
    /// 內容清單的批次操作：整批以同一資料庫狀態驗證一次授權，再逐筆執行
    /// 既有單筆核心流程（含各自的並發版本檢查與人工確認契約），逐筆回報結果；
    /// 單筆失敗不中斷其餘項目。
    /// </summary>
    public async Task<List<BulkItemResult>> BulkAsync(BulkAction action, IReadOnlyList<(Guid Id, Guid Version)> items, CancellationToken ct = default)
    {
        // 批次視為單一管理操作：開始前完整重驗一次，不逐筆重複同樣的驗證查詢；
        // 逐筆核心流程仍受站點過濾與並發版本約束保護。
        await admin.RequireAsync(ct);
        if (items.Count == 0) throw new CmsException("請先選擇要操作的內容。");
        if (items.Count > Limits.BulkMaxItems) throw new CmsException($"單次批次操作最多 {Limits.BulkMaxItems} 筆。");
        if (!Enum.IsDefined(action)) throw new CmsException("未知的批次操作。");
        List<BulkItemResult> results = new(items.Count);
        foreach (var (id, version) in items)
        {
            try
            {
                switch (action)
                {
                    case BulkAction.Trash:
                        await TrashCoreAsync(id, version, ct);
                        break;
                    case BulkAction.Restore:
                        await RestoreCoreAsync(id, version, ct);
                        break;
                    case BulkAction.Delete:
                        await DeleteCoreAsync(id, version, ct);
                        break;
                }
                results.Add(new BulkItemResult(id, true, ""));
            }
            catch (CmsException error)
            {
                results.Add(new BulkItemResult(id, false, error.Message));
            }
        }
        return results;
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
        return await SiteAdministrationService.WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow();
            var cutoff = now.AddDays(-Limits.TrashRetentionDays);
            var candidates = await db.Contents.Where(x => x.Status == ContentStatus.Trashed && x.DeletedAt < cutoff)
                .Select(x => x.Id).ToArrayAsync(ct);
            if (candidates.Length == 0) return 0;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockPageRowsAsync(db, ct);
            // 等候列鎖之後重新判斷狀態；已還原的內容及其子頁關聯都必須保留。
            var expired = await db.Contents.FromSqlInterpolated($"""
                SELECT * FROM "Contents"
                WHERE "SiteId" = {db.CurrentSiteId} AND "Id" = ANY ({candidates})
                  AND "Status" = {(int)ContentStatus.Trashed} AND "DeletedAt" < {cutoff}
                ORDER BY "Id" FOR UPDATE
                """).AsNoTracking().Select(x => x.Id).ToArrayAsync(ct);
            if (expired.Length == 0) return 0;
            await ClearContentReferencesAsync(db, expired, now, ct);
            var count = await db.Contents.Where(x => expired.Contains(x.Id)
                    && x.Status == ContentStatus.Trashed && x.DeletedAt < cutoff).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
            return count;
        });
    }

    public async Task<List<RevisionItem>> ListRevisionsAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentRevisions.AsNoTracking().Where(x => x.ContentId == contentId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(Limits.RevisionKeep)
            .Select(x => new RevisionItem(x.Id, x.Title, x.CreatedAt)).ToListAsync(ct);
    }

    public async Task<RevisionDetail?> GetRevisionAsync(Guid revisionId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentRevisions.AsNoTracking().Where(x => x.Id == revisionId)
            .Select(x => new RevisionDetail(x.Id, x.ContentId, x.Title, x.Markdown, x.Excerpt, x.CreatedAt)).SingleOrDefaultAsync(ct);
    }

    /// <summary>替換分享 token；舊連結立即失效。</summary>
    public async Task<Guid> CyclePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        return (await ModifyPreviewTokenAsync(contentId, Guid.NewGuid(), ct))!.Value;
    }

    /// <summary>移除分享 token；預覽連結立即失效。</summary>
    public async Task RevokePreviewTokenAsync(Guid contentId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Author, ct);
        await ModifyPreviewTokenAsync(contentId, null, ct);
    }

    private async Task<Guid?> ModifyPreviewTokenAsync(Guid contentId, Guid? token, CancellationToken ct)
    {
        var access = await admin.GetCurrentAccessAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == contentId, ct) ?? throw new CmsException("內容不存在。");
        RequireAuthorEditable(entity, access);
        if (entity.Status == ContentStatus.Trashed) throw new CmsException("回收桶內容不可建立預覽連結。");
        var now = clock.GetUtcNow();
        entity.PreviewToken = token;
        entity.UpdatedAt = now;
        // 分享 token 不屬於被編輯的內容，因此並發版本保持不變；
        // 平行的內容儲存仍必須以衝突收場，不得靜默覆寫。
        await ContentRules.SaveAsync(db, ct);
        return entity.PreviewToken;
    }

    /// <summary>透過分享 token 可讀取的草稿與待審內容；回收桶與已發布內容不會在此渲染。</summary>
    public async Task<PublicContent?> GetPreviewAsync(Guid token, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery()
            .Where(x => x.PreviewToken == token && (x.Status == ContentStatus.Draft || x.Status == ContentStatus.Pending))
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => new NavigationItem(c.Name, c.Slug)).ToList(),
                x.Tags.OrderBy(t => t.Name).Select(t => new NavigationItem(t.Name, t.Slug)).ToList())
            { Excerpt = x.Excerpt,
                MetaDescription = x.MetaDescription, NoIndex = x.NoIndex, UpdatedAt = x.UpdatedAt,
                AuthorName = x.AuthorName, AuthorSlug = x.AuthorSlug,
                CoverFileName = x.Cover != null ? x.Cover.FileName : "",
                CoverAltText = x.Cover != null ? x.Cover.AltText : "",
                CoverCaption = x.Cover != null ? x.Cover.Caption : "" }).SingleOrDefaultAsync(ct);
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
        CancellationToken ct = default, string? category = null, string? tag = null, int? year = null, int? month = null, string? author = null)
    {
        var offset = ContentRules.Offset(page, pageSize);
        if (category is not null) category = ContentRules.NormalizeSlug(category);
        if (tag is not null) tag = ContentRules.NormalizeSlug(tag);
        if (author is not null) author = ContentRules.NormalizeSlug(author);
        if (month is { } m && m is < 1 or > 12) throw new CmsException("月份無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published
            && !x.IsPrivate && x.ContentTypeId == null);
        if (category is not null) query = query.Where(x => x.Categories.Any(c => c.Slug == category));
        if (tag is not null) query = query.Where(x => x.Tags.Any(t => t.Slug == tag));
        if (author is not null) query = query.Where(x => x.AuthorSlug == author);
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
                IsProtected = x.IsProtected,
                CoverFileName = x.Cover != null ? x.Cover.FileName : "",
                CoverAltText = x.Cover != null ? x.Cover.AltText : ""
            }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    /// <summary>訂閱資料只投影當站可公開正文；篩選、排序及筆數上限在同一查詢完成。</summary>
    public async Task<List<FeedEntry>> FeedAsync(int count, string? category = null, string? tag = null, CancellationToken ct = default)
    {
        if (count is < 1 or > Limits.MaxFeedItems) throw new CmsException("訂閱筆數超出範圍。");
        if (category is not null) category = ContentRules.NormalizeSlug(category);
        if (tag is not null) tag = ContentRules.NormalizeSlug(tag);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Post &&
            x.Status == ContentStatus.Published && !x.IsPrivate && !x.IsProtected && x.ContentTypeId == null);
        if (category is not null) query = query.Where(x => x.Categories.Any(c => c.Slug == category));
        if (tag is not null) query = query.Where(x => x.Tags.Any(t => t.Slug == tag));
        return await query.OrderByDescending(x => x.IsSticky).ThenByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(count).Select(x => new FeedEntry(x.Id, x.Title, x.Slug, x.PublishedAt,
                x.Excerpt != "" ? x.Excerpt : x.Markdown.Substring(0, x.Markdown.Length > 600 ? 600 : x.Markdown.Length),
                x.Markdown, x.Excerpt != "", x.AuthorName)).ToListAsync(ct);
    }

    public sealed record FeedEntry(Guid Id, string Title, string Slug, DateTimeOffset? PublishedAt,
        string Excerpt, string Markdown, bool ExcerptIsManual, string AuthorName = "");

    public async Task<List<PublicTaxonomy>> PopularCategoriesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking()
            .Select(x => new { x.Name, x.Slug, Count = x.Contents.Count(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate && c.ContentTypeId == null) })
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
            ? await db.Categories.AsNoTracking().Where(x => x.Slug == slug && x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate && c.ContentTypeId == null))
                .Select(x => new TaxonomyView(x.Name, x.Slug, x.Description)).SingleOrDefaultAsync(ct)
            : await db.Tags.AsNoTracking().Where(x => x.Slug == slug && x.Contents.Any(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate && c.ContentTypeId == null))
                .Select(x => new TaxonomyView(x.Name, x.Slug, x.Description)).SingleOrDefaultAsync(ct);
    }

    public async Task<PublicContent?> GetPublishedAsync(ContentKind kind, string slug, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery().Where(x => x.Kind == kind && x.Status == ContentStatus.Published
                && x.Slug == slug && x.ContentTypeId == null)
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => new NavigationItem(c.Name, c.Slug)).ToList(),
                x.Tags.OrderBy(t => t.Name).Select(t => new NavigationItem(t.Name, t.Slug)).ToList())
            { Excerpt = x.Excerpt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate,
                MetaDescription = x.MetaDescription, NoIndex = x.NoIndex, UpdatedAt = x.UpdatedAt,
                AuthorName = x.AuthorName, AuthorSlug = x.AuthorSlug,
                CoverFileName = x.Cover != null ? x.Cover.FileName : "",
                CoverAltText = x.Cover != null ? x.Cover.AltText : "",
                CoverCaption = x.Cover != null ? x.Cover.Caption : "",
                Fields = x.FieldValues.OrderBy(v => v.Field.Position).ThenBy(v => v.Field.Key)
                    .Select(v => new PublicFieldValue(v.Field.Name, v.Field.Key, v.Value, v.Field.FieldType)).ToList() })
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// 某個自訂內容類型的已發布內容清單；類型不存在或已封存時回傳 null。
    /// 自訂類型內容不混入內建文章列表，僅在自己的公開區塊出現。
    /// </summary>
    public async Task<PagedResult<ContentSummary>?> ListByTypeAsync(string? typeSlug, int page = 1,
        int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeSlug)) return null;
        var normalized = ContentRules.NormalizeSlug(typeSlug);
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var type = await db.ContentTypes.AsNoTracking().Where(x => x.Slug == normalized && !x.IsArchived)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (!type.HasValue) return null;
        var query = db.Contents.AsNoTracking().Where(x => x.ContentTypeId == type
            && x.Status == ContentStatus.Published && !x.IsPrivate);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize)
            .Select(x => new ContentSummary(x.Id, x.Kind, x.Title, x.Slug, x.Status, x.UpdatedAt, x.PublishedAt, x.Version)
            {
                Excerpt = x.IsProtected ? "此內容受密碼保護。" : x.Excerpt != "" ? x.Excerpt : x.Markdown.Substring(0, x.Markdown.Length > 600 ? 600 : x.Markdown.Length),
                ExcerptIsManual = x.IsProtected || x.Excerpt != "",
                IsProtected = x.IsProtected,
                CoverFileName = x.Cover != null ? x.Cover.FileName : "",
                CoverAltText = x.Cover != null ? x.Cover.AltText : "",
                AuthorName = x.AuthorName, AuthorSlug = x.AuthorSlug,
                MetaDescription = x.MetaDescription, NoIndex = x.NoIndex,
                ParentId = x.ParentId
            }).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    /// <summary>某個自訂內容類型下的已發布內容；類型、網址代稱或狀態不符時回傳 null。</summary>
    public async Task<PublicContent?> GetPublishedByTypeAsync(string? typeSlug, string? slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeSlug) || string.IsNullOrWhiteSpace(slug)) return null;
        var normalizedType = ContentRules.NormalizeSlug(typeSlug);
        var normalizedSlug = ContentRules.NormalizeSlug(slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        var type = await db.ContentTypes.AsNoTracking().Where(x => x.Slug == normalizedType && !x.IsArchived)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (!type.HasValue) return null;
        return await db.Contents.AsNoTracking().AsSplitQuery()
            .Where(x => x.ContentTypeId == type && x.Status == ContentStatus.Published && x.Slug == normalizedSlug)
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => new NavigationItem(c.Name, c.Slug)).ToList(),
                x.Tags.OrderBy(t => t.Name).Select(t => new NavigationItem(t.Name, t.Slug)).ToList())
            { Excerpt = x.Excerpt, IsProtected = x.IsProtected, IsPrivate = x.IsPrivate,
                MetaDescription = x.MetaDescription, NoIndex = x.NoIndex, UpdatedAt = x.UpdatedAt,
                AuthorName = x.AuthorName, AuthorSlug = x.AuthorSlug,
                CoverFileName = x.Cover != null ? x.Cover.FileName : "",
                CoverAltText = x.Cover != null ? x.Cover.AltText : "",
                CoverCaption = x.Cover != null ? x.Cover.Caption : "",
                Fields = x.FieldValues.OrderBy(v => v.Field.Position).ThenBy(v => v.Field.Key)
                    .Select(v => new PublicFieldValue(v.Field.Name, v.Field.Key, v.Value, v.Field.FieldType)).ToList() })
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// 以路徑段解析已發布頁面：最後一段是頁面 slug，前段必須依序符合其已發布祖先。
    /// 祖先未發布或私密時子頁仍以平面形式提供（Segments 僅含自身，與 sitemap／導覽一致）；
    /// 鏈上段數不符由呼叫端決定 404 或轉址。
    /// </summary>
    public async Task<PagePathContent?> ResolvePublishedPagePathAsync(string[] segments, CancellationToken ct = default)
    {
        if (segments.Length is < 1 or > 6) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var slug = segments[^1];
        var target = await db.Contents.AsNoTracking()
            .Where(x => x.Kind == ContentKind.Page && x.Status == ContentStatus.Published && x.Slug == slug)
            .Select(x => new { x.Id, x.ParentId }).SingleOrDefaultAsync(ct);
        if (target is null) return null;
        // 由下往上蒐集已發布且非私密的祖先；遇到未發布、私密或缺失的祖先即停止
        // （子頁退回平面網址——私密頁 slug 不得出現在公開鏈上）。
        var chain = new List<string>();
        Guid? cursor = target.ParentId;
        var guard = 0;
        while (cursor is { } currentId && ++guard <= 10)
        {
            var node = await db.Contents.AsNoTracking()
                .Where(x => x.Id == currentId && x.Kind == ContentKind.Page && x.Status == ContentStatus.Published && !x.IsPrivate)
                .Select(x => new { x.Slug, x.ParentId }).SingleOrDefaultAsync(ct);
            if (node is null) break;
            chain.Add(node.Slug);
            cursor = node.ParentId;
        }
        chain.Reverse();
        var content = await GetPublishedAsync(ContentKind.Page, slug, ct);
        return content is null ? null : new PagePathContent(content, [.. chain, slug]);
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
                INSERT INTO "ContentViews" ("SiteId", "ContentId", "Date", "Count")
                SELECT "SiteId", "Id", {date}, 1 FROM "Contents"
                WHERE "SiteId" = {db.CurrentSiteId} AND "Id" = {contentId} AND "Status" = {(int)ContentStatus.Published}
                ON CONFLICT ("ContentId", "Date") DO UPDATE SET "Count" = "ContentViews"."Count" + 1
                WHERE "ContentViews"."SiteId" = {db.CurrentSiteId}
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
        // 一次投影足夠的欄位：文章平面路徑、頁面階層路徑與各自的更新時間。
        var rows = await db.Contents.AsNoTracking()
            .Where(x => x.Status == ContentStatus.Published && !x.IsPrivate && !x.NoIndex)
            .Select(x => new { x.Id, x.Kind, x.Slug, x.ParentId, x.UpdatedAt, x.ContentTypeId }).ToListAsync(ct);
        var typeSlugs = await db.ContentTypes.AsNoTracking().Where(x => !x.IsArchived)
            .Select(x => new { x.Id, x.Slug }).ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
        var pages = rows.Where(x => x.Kind == ContentKind.Page).ToDictionary(x => x.Id);
        // 頁面公開路徑走訪已發布且非私密的祖先；未發布或私密祖先使子頁回退為平面路徑（與公開路由一致）。
        string PagePath(Guid id)
        {
            var parts = new List<string>();
            Guid? cursor = id;
            var guard = 0;
            while (cursor is { } current && pages.TryGetValue(current, out var node) && ++guard <= 10)
            {
                parts.Insert(0, node.Slug);
                cursor = node.ParentId;
            }
            return string.Join("/", parts);
        }
        var categories = await db.Categories.AsNoTracking()
            .Select(x => new { x.Slug, Latest = x.Contents
                .Where(c => c.Kind == ContentKind.Post && c.Status == ContentStatus.Published && !c.IsPrivate)
                .Max(c => (DateTimeOffset?)c.PublishedAt) })
            .Where(x => x.Latest != null).ToListAsync(ct);
        var homeLastModified = rows.Count == 0 ? null : (DateTimeOffset?)rows.Max(x => x.UpdatedAt);
        List<SitemapItem> items = [new("/", homeLastModified), new("/archives", homeLastModified)];
        items.AddRange(rows.Where(x => x.Kind == ContentKind.Post && x.ContentTypeId == null).Select(x => new SitemapItem("/posts/" + x.Slug, x.UpdatedAt)));
        items.AddRange(rows.Where(x => x.Kind == ContentKind.Post && x.ContentTypeId is { } typeId && typeSlugs.ContainsKey(typeId))
            .Select(x => new SitemapItem("/" + ContentTypeService.PublicPrefix + "/" + typeSlugs[x.ContentTypeId!.Value] + "/" + x.Slug, x.UpdatedAt)));
        items.AddRange(pages.Values.Select(x => new SitemapItem("/pages/" + PagePath(x.Id), x.UpdatedAt)));
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
