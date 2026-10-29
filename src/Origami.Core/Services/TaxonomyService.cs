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
            var items = await db.Categories.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new TaxonomyItem(x.Id, x.Name, x.Slug, x.Version)
                {
                    Description = x.Description,
                    ParentId = x.ParentId,
                    ParentName = x.Parent != null ? x.Parent.Name : ""
                }).ToListAsync(ct);
            return new(items, total, page, pageSize);
        }
        else
        {
            var total = await db.Tags.CountAsync(ct);
            var items = await db.Tags.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new TaxonomyItem(x.Id, x.Name, x.Slug, x.Version) { Description = x.Description }).ToListAsync(ct);
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
        List<Guid> sourceIds;
        HashSet<Guid> targetIds;
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
            sourceIds = await db.Categories.Where(x => x.Id == sourceId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct);
            targetIds = new HashSet<Guid>(await db.Categories.Where(x => x.Id == targetId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct));
            return await MoveAndRemoveAsync(db, source, target.Contents, sourceIds, targetIds, ct);
        }
        else
        {
            var source = await db.Tags.SingleOrDefaultAsync(x => x.Id == sourceId, ct)
                ?? throw new CmsException("來源標籤不存在。");
            var target = await db.Tags.SingleOrDefaultAsync(x => x.Id == targetId, ct)
                ?? throw new CmsException("目標標籤不存在。");
            db.Entry(source).Property(x => x.Version).OriginalValue = version;
            sourceIds = await db.Tags.Where(x => x.Id == sourceId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct);
            targetIds = new HashSet<Guid>(await db.Tags.Where(x => x.Id == targetId).SelectMany(x => x.Contents)
                .Select(x => x.Id).ToListAsync(ct));
            return await MoveAndRemoveAsync(db, source, target.Contents, sourceIds, targetIds, ct);
        }
    }

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
        if (kind == TaxonomyKind.Category)
        {
            var entity = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("分類不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            // 刪除父分類時，子分類自動提升到被刪分類的上層（WordPress 慣例）。
            await db.Categories.Where(x => x.ParentId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, entity.ParentId)
                    .SetProperty(x => x.Version, Guid.NewGuid()), ct);
            db.Categories.Remove(entity);
        }
        else
        {
            var entity = await db.Tags.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("標籤不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            db.Tags.Remove(entity);
        }
        await ContentRules.SaveAsync(db, ct);
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
