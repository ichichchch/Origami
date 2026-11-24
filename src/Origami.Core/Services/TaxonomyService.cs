namespace Origami.Core.Services;

public sealed class TaxonomyService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public async Task<PagedResult<TaxonomyItem>> ListAsync(TaxonomyKind kind, int page = 1, int pageSize = Limits.MaxPageSize, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        ValidateKind(kind);
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (kind == TaxonomyKind.Category)
        {
            var total = await db.Categories.CountAsync(ct);
            var items = await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new TaxonomyItem(x.Id, x.Name, x.Slug, x.Version)
                {
                    Description = x.Description,
                    ParentId = x.ParentId,
                    ParentName = x.Parent != null ? x.Parent.Name : "",
                    SortOrder = x.SortOrder
                }).ToListAsync(ct);
            return new(items, total, page, pageSize);
        }
        else
        {
            var total = await db.Tags.CountAsync(ct);
            var items = await db.Tags.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new TaxonomyItem(x.Id, x.Name, x.Slug, x.Version) { Description = x.Description, SortOrder = x.SortOrder }).ToListAsync(ct);
            return new(items, total, page, pageSize);
        }
    }

    public async Task<TaxonomyEdit> SaveAsync(TaxonomyKind kind, TaxonomyEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        ContentRules.Validate(input);
        var slug = ContentRules.NormalizeSlug(input.Id is null && string.IsNullOrWhiteSpace(input.Slug) ? input.Name : input.Slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        var version = Guid.NewGuid();
        Guid id;
        if (kind == TaxonomyKind.Category)
        {
            var entity = input.Id is { } existingId
                ? await db.Categories.SingleOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new CmsException("分類不存在。")
                : new Category();
            if (input.Id is null) db.Categories.Add(entity);
            else db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            if (input.ParentId is { } parentId)
            {
                if (parentId == entity.Id) throw new CmsException("分類不可設為自己的上層。");
                var all = await db.Categories.AsNoTracking().Select(x => new { x.Id, x.ParentId }).ToListAsync(ct);
                var byId = all.ToDictionary(x => x.Id);
                if (!byId.ContainsKey(parentId)) throw new CmsException("上層分類不存在，請重新選擇。");
                var guard = 0;
                Guid? cursor = parentId;
                while (cursor is not null)
                {
                    if (cursor == entity.Id) throw new CmsException("不可將分類設為其子孫的下層，這會形成循環階層。");
                    if (++guard > 100) throw new CmsException("分類階層過深，請檢查上層設定。");
                    cursor = byId.TryGetValue(cursor.Value, out var node) ? node.ParentId : null;
                }
            }
            entity.Name = input.Name.Trim();
            entity.Slug = slug;
            entity.Description = (input.Description ?? "").Trim();
            entity.ParentId = input.ParentId;
            entity.SortOrder = input.SortOrder;
            entity.Version = version;
            id = entity.Id;
        }
        else
        {
            if (input.ParentId is not null) throw new CmsException("標籤不支援上層階層。");
            var entity = input.Id is { } existingId
                ? await db.Tags.SingleOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new CmsException("標籤不存在。")
                : new Tag();
            if (input.Id is null) db.Tags.Add(entity);
            else db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            entity.Name = input.Name.Trim();
            entity.Slug = slug;
            entity.Description = (input.Description ?? "").Trim();
            entity.SortOrder = input.SortOrder;
            entity.Version = version;
            id = entity.Id;
        }
        await ContentRules.SaveAsync(db, ct);
        input.Id = id;
        input.Slug = slug;
        input.Version = version;
        return input;
    }

    /// <summary>
    /// 合併：把來源的文章關聯改指到目標，然後刪除來源；文章本身不動，
    /// 目標的內容（名稱、代稱、描述）也不變。整個作業在單一交易內完成。
    /// 回傳新指到目標的文章數；原已同屬目標者僅移除來源關聯，不計入。
    /// </summary>
    public async Task<int> MergeAsync(TaxonomyKind kind, Guid sourceId, Guid targetId, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        if (sourceId == targetId) throw new CmsException("不可合併到自身。");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        int moved;
        if (kind == TaxonomyKind.Category)
        {
            var source = await db.Categories.SingleOrDefaultAsync(x => x.Id == sourceId, ct)
                ?? throw new CmsException("來源分類不存在。");
            var target = await db.Categories.SingleOrDefaultAsync(x => x.Id == targetId, ct)
                ?? throw new CmsException("目標分類不存在。");
            db.Entry(source).Property(x => x.Version).OriginalValue = version;
            // 若目標分類原本位於來源分類的子樹下，先把來源的直屬子分類提升到來源的父層，避免循環與外鍵阻擋。
            await db.Categories.Where(x => x.ParentId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, source.ParentId)
                    .SetProperty(x => x.Version, Guid.NewGuid()), ct);
            var sourceIds = await db.Categories.Where(x => x.Id == sourceId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct);
            var targetIds = new HashSet<Guid>(await db.Categories.Where(x => x.Id == targetId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct));
            await MoveDefaultAsync(db, kind, sourceId, targetId, ct);
            await MoveNavigationAsync(db, kind, sourceId, targetId, ct);
            moved = await MoveAndRemoveAsync(db, source, target.Contents, sourceIds, targetIds, ct);
        }
        else
        {
            var source = await db.Tags.SingleOrDefaultAsync(x => x.Id == sourceId, ct)
                ?? throw new CmsException("來源標籤不存在。");
            var target = await db.Tags.SingleOrDefaultAsync(x => x.Id == targetId, ct)
                ?? throw new CmsException("目標標籤不存在。");
            db.Entry(source).Property(x => x.Version).OriginalValue = version;
            var sourceIds = await db.Tags.Where(x => x.Id == sourceId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct);
            var targetIds = new HashSet<Guid>(await db.Tags.Where(x => x.Id == targetId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct));
            await MoveDefaultAsync(db, kind, sourceId, targetId, ct);
            await MoveNavigationAsync(db, kind, sourceId, targetId, ct);
            moved = await MoveAndRemoveAsync(db, source, target.Contents, sourceIds, targetIds, ct);
        }
        await transaction.CommitAsync(ct);
        return moved;
    }

    /// <summary>
    /// 把標籤轉為分類：沿用名稱、代稱與描述建立分類（已有同代稱或同名分類時直接沿用），
    /// 關聯文章改指到該分類後刪除標籤；原已屬該分類者只移除標籤關聯。回傳目標分類。
    /// </summary>
    public async Task<TaxonomyItem> ConvertTagToCategoryAsync(Guid tagId, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tag = await db.Tags.SingleOrDefaultAsync(x => x.Id == tagId, ct) ?? throw new CmsException("標籤不存在。");
        db.Entry(tag).Property(x => x.Version).OriginalValue = version;
        var category = await db.Categories.FirstOrDefaultAsync(x => x.Slug == tag.Slug, ct)
            ?? await db.Categories.FirstOrDefaultAsync(x => x.Name == tag.Name, ct);
        if (category is null)
        {
            category = new Category { Name = tag.Name, Slug = tag.Slug, Description = tag.Description };
            db.Categories.Add(category);
        }
        var categoryId = category.Id;
        var existing = new HashSet<Guid>(await db.Categories.Where(x => x.Id == categoryId).SelectMany(x => x.Contents)
            .Select(x => x.Id).ToListAsync(ct));
        var contentIds = await db.Tags.Where(x => x.Id == tagId).SelectMany(x => x.Contents).Select(x => x.Id).ToListAsync(ct);
        foreach (var contentId in contentIds.Where(x => !existing.Contains(x)))
        {
            // 只以識別 stub 建立目標關聯，不載入文章實體；stub 必須先 Attach，否則會被當成新內容插入。
            var stub = new Content { Id = contentId, SiteId = db.CurrentSiteId };
            db.Contents.Attach(stub);
            category.Contents.Add(stub);
        }
        db.Tags.Remove(tag);
        await db.SiteNavItems.Where(x => x.TagId == tagId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CategoryId, categoryId)
                .SetProperty(x => x.TagId, (Guid?)null)
                .SetProperty(x => x.Version, Guid.NewGuid()), ct);
        // 被刪除的標籤若是站點預設標籤，設定一併清空。
        await ClearDefaultAsync(db, TaxonomyKind.Tag, tagId, ct);
        await ContentRules.SaveAsync(db, ct);
        await transaction.CommitAsync(ct);
        return new TaxonomyItem(category.Id, category.Name, category.Slug, category.Version) { Description = category.Description };
    }

    /// <summary>刪除分類或標籤時，清空指向它的站點預設設定。</summary>
    private static Task ClearDefaultAsync(CmsDbContext db, TaxonomyKind kind, Guid id, CancellationToken ct) =>
        kind == TaxonomyKind.Category
            ? db.Settings.Where(x => x.DefaultCategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultCategoryId, (Guid?)null), ct)
            : db.Settings.Where(x => x.DefaultTagId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultTagId, (Guid?)null), ct);

    /// <summary>目標詞彙被刪除時移除失效導覽項目，並像手動刪除一樣把子項目提升到上層。</summary>
    private static async Task RemoveNavigationAsync(CmsDbContext db, TaxonomyKind kind, Guid id, CancellationToken ct)
    {
        var ids = kind == TaxonomyKind.Category
            ? await db.SiteNavItems.AsNoTracking().Where(x => x.CategoryId == id).Select(x => x.Id).ToListAsync(ct)
            : await db.SiteNavItems.AsNoTracking().Where(x => x.TagId == id).Select(x => x.Id).ToListAsync(ct);
        foreach (var navId in ids)
        {
            var parentId = await db.SiteNavItems.AsNoTracking().Where(x => x.Id == navId).Select(x => x.ParentId).SingleAsync(ct);
            await db.SiteNavItems.Where(x => x.ParentId == navId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, parentId)
                    .SetProperty(x => x.Version, Guid.NewGuid()), ct);
            await db.SiteNavItems.Where(x => x.Id == navId).ExecuteDeleteAsync(ct);
        }
    }

    /// <summary>合併後來源被刪除：站點預設改指向目標，新文章不會因此失去預設值。</summary>
    private static Task MoveDefaultAsync(CmsDbContext db, TaxonomyKind kind, Guid sourceId, Guid targetId, CancellationToken ct) =>
        kind == TaxonomyKind.Category
            ? db.Settings.Where(x => x.DefaultCategoryId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultCategoryId, targetId), ct)
            : db.Settings.Where(x => x.DefaultTagId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultTagId, targetId), ct);

    /// <summary>合併後把來源詞彙的導覽目標改指到目標詞彙。</summary>
    private static Task MoveNavigationAsync(CmsDbContext db, TaxonomyKind kind, Guid sourceId, Guid targetId, CancellationToken ct) =>
        kind == TaxonomyKind.Category
            ? db.SiteNavItems.Where(x => x.CategoryId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CategoryId, targetId)
                    .SetProperty(x => x.Version, Guid.NewGuid()), ct)
            : db.SiteNavItems.Where(x => x.TagId == sourceId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.TagId, targetId)
                    .SetProperty(x => x.Version, Guid.NewGuid()), ct);

    private static async Task<int> MoveAndRemoveAsync(CmsDbContext db, object source, ICollection<Content> targetContents,
        List<Guid> sourceIds, HashSet<Guid> targetIds, CancellationToken ct)
    {
        var moved = 0;
        foreach (var id in sourceIds)
        {
            // 原已屬於目標者只移除來源關聯（隨來源刪除級聯清理），不重複加入也不計入移轉數。
            if (targetIds.Contains(id)) continue;
            // 只以識別 stub 建立目標關聯，不載入文章實體（避免把 Markdown 全文整批載入記憶體）。
            var stub = new Content { Id = id, SiteId = db.CurrentSiteId };
            db.Contents.Attach(stub);
            targetContents.Add(stub);
            moved++;
        }
        // 刪除來源會級聯刪除其全部關聯列；「原屬來源＋目標」者最後只剩目標一條，不產生重複。
        db.Remove(source);
        await ContentRules.SaveAsync(db, ct);
        return moved;
    }

    public async Task DeleteAsync(TaxonomyKind kind, Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (kind == TaxonomyKind.Category)
        {
            var entity = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("分類不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            // 刪除父分類時，子分類自動提升到被刪分類的上層（WordPress 慣例）。
            await db.Categories.Where(x => x.ParentId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, entity.ParentId)
                    .SetProperty(x => x.Version, Guid.NewGuid()), ct);
            await RemoveNavigationAsync(db, kind, id, ct);
            db.Categories.Remove(entity);
            await ClearDefaultAsync(db, kind, id, ct);
        }
        else
        {
            var entity = await db.Tags.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("標籤不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            await RemoveNavigationAsync(db, kind, id, ct);
            db.Tags.Remove(entity);
            await ClearDefaultAsync(db, kind, id, ct);
        }
        await ContentRules.SaveAsync(db, ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>批次刪除分類或標籤：逐筆攜帶並發版本、逐筆回報結果；刪除只移除關聯，不刪文章。</summary>
    public async Task<List<BulkItemResult>> BulkDeleteAsync(TaxonomyKind kind, IReadOnlyList<(Guid Id, Guid Version)> items, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        if (items.Count == 0) throw new CmsException("請先選擇要刪除的項目。");
        if (items.Count > Limits.BulkMaxItems) throw new CmsException($"單次批次操作最多 {Limits.BulkMaxItems} 筆。");
        List<BulkItemResult> results = new(items.Count);
        foreach (var (id, version) in items)
        {
            try
            {
                await DeleteAsync(kind, id, version, ct);
                results.Add(new BulkItemResult(id, true, ""));
            }
            catch (CmsException error)
            {
                results.Add(new BulkItemResult(id, false, error.Message));
            }
        }
        return results;
    }

    private static void ValidateKind(TaxonomyKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new CmsException("分類類型無效。");
    }
}
