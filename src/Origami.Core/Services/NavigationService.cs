namespace Origami.Core.Services;

/// <summary>
/// 受控導覽：自訂項目可指向站內頁面或外部連結，可排序、可隱藏。
/// 設定未啟用自訂導覽時，公開導覽沿用「已發布頁面依標題排序並帶階層」的預設行為。
/// </summary>
public sealed class NavigationService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public async Task<List<NavItemView>> ListAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.SiteNavItems.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Id)
            .Select(x => new NavItemView(x.Id, x.Title, x.ContentId, x.Url, x.IsVisible, x.Position, x.Version)
            {
                ParentId = x.ParentId, Location = x.Location, CategoryId = x.CategoryId, TagId = x.TagId,
                ToArchives = x.ToArchives, AuthorSlug = x.AuthorSlug
            })
            .ToListAsync(ct);
        return items;
    }

    /// <summary>建立或更新導覽項目；站內頁面與外部連結二者擇一。</summary>
    public async Task<NavItemView> SaveAsync(NavItemInput input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        var title = (input.Title ?? "").Trim();
        if (title.Length is 0 or > Limits.NavTitle) throw new CmsException($"導覽文字須為 1–{Limits.NavTitle} 字。");
        var url = (input.Url ?? "").Trim();
        if (url.Length > Limits.NavUrl) throw new CmsException($"導覽網址最長 {Limits.NavUrl} 字。");
        await using var db = await factory.CreateDbContextAsync(ct);
        // 目標恰有 one：站內頁面、外部連結、分類、標籤、歸檔或作者。
        var authorSlug = string.IsNullOrWhiteSpace(input.AuthorSlug) ? "" : ContentRules.NormalizeSlug(input.AuthorSlug);
        var targetCount = new[] { input.ContentId is not null, input.CategoryId is not null, input.TagId is not null, input.ToArchives, authorSlug.Length > 0, url.Length > 0 }.Count(x => x);
        if (targetCount != 1) throw new CmsException("導覽項目須指定恰好一個目標：站內頁面、外部連結、分類、標籤、歸檔或作者。");
        if (input.Location is not (0 or 1)) throw new CmsException("導覽位置無效（頁首或頁尾）。");
        if (input.CategoryId is { } categoryId && !await db.Categories.AsNoTracking().AnyAsync(x => x.Id == categoryId, ct))
            throw new CmsException("分類不存在。");
        if (input.TagId is { } tagId && !await db.Tags.AsNoTracking().AnyAsync(x => x.Id == tagId, ct))
            throw new CmsException("標籤不存在。");
        if (authorSlug.Length > 0 && !await db.Contents.AsNoTracking()
                .AnyAsync(x => x.AuthorSlug == authorSlug && x.Status == ContentStatus.Published && x.DeletedAt == null, ct))
            throw new CmsException("作者不存在或尚無已發布文章。");
        if (input.ParentId is { } parentId)
        {
            if (input.Id == parentId) throw new CmsException("導覽項目不可設為自己的上層。");
            var parent = await db.SiteNavItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parentId, ct)
                ?? throw new CmsException("上層導覽項目不存在。");
            if (parent.ParentId is not null) throw new CmsException("導覽最多兩層，子項目不可再有自己的子項目。");
        }
        if (input.ContentId is { } target)
        {
            // 站內目標必須是本站已發布、未受保護且非私密的頁面。
            var page = await db.Contents.AsNoTracking().Where(x => x.Id == target)
                .Select(x => new { x.Kind, x.Status, x.IsPrivate, x.IsProtected }).SingleOrDefaultAsync(ct);
            if (page is null || page.Kind != ContentKind.Page || page.Status != ContentStatus.Published
                || page.IsPrivate || page.IsProtected)
                throw new CmsException("導覽只能指向本站已發布的獨立頁面。");
            url = "";
        }
        else if (url.Length > 0)
        {
            url = ContentRules.OptionalHttpUrl(url, Limits.NavUrl, "外部連結");
        }

        SiteNavItem entity;
        if (input.Id is { } existing)
        {
            entity = await db.SiteNavItems.SingleOrDefaultAsync(x => x.Id == existing, ct) ?? throw new CmsException("導覽項目不存在。");
            if (entity.Version != input.Version) throw new CmsException("資料已被另一個分頁更新，請重新載入後再編輯。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            entity = new SiteNavItem { Position = await db.SiteNavItems.AnyAsync(ct) ? await db.SiteNavItems.MaxAsync(x => x.Position, ct) + 1 : 0 };
            db.SiteNavItems.Add(entity);
        }
        entity.Title = title;
        // 外部網址只在外部連結項目上保留；其他目標類型一律清空（資料庫檢查約束亦要求）。
        entity.Url = input.ContentId is null && input.CategoryId is null && input.TagId is null && !input.ToArchives && authorSlug.Length == 0 ? url : "";
        entity.IsVisible = input.IsVisible;
        entity.ContentId = input.ContentId;
        entity.ParentId = input.ParentId;
        entity.Location = input.Location;
        entity.CategoryId = input.CategoryId;
        entity.TagId = input.TagId;
        entity.ToArchives = input.ToArchives;
        entity.AuthorSlug = authorSlug;
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
        return new(entity.Id, entity.Title, entity.ContentId, entity.Url, entity.IsVisible, entity.Position, entity.Version)
        {
            ParentId = entity.ParentId, Location = entity.Location, CategoryId = entity.CategoryId, TagId = entity.TagId,
            ToArchives = entity.ToArchives, AuthorSlug = entity.AuthorSlug
        };
    }

    public async Task DeleteAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.SiteNavItems.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("導覽項目不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        // 子項目上移到被刪項目的上層，不隨之消失。
        await db.SiteNavItems.Where(x => x.ParentId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, entity.ParentId).SetProperty(x => x.Version, Guid.NewGuid()), ct);
        db.SiteNavItems.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>依提交的順序重新編號；逐筆攜帶版本，單筆失敗不影響其他項目。</summary>
    public async Task<List<BulkItemResult>> ReorderAsync(IReadOnlyList<(Guid Id, Guid Version)> items, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        if (items.Count == 0) throw new CmsException("請先選擇要排序的項目。");
        if (items.Count > Limits.BulkMaxItems) throw new CmsException($"單次批次操作最多 {Limits.BulkMaxItems} 筆。");
        var results = new List<BulkItemResult>(items.Count);
        await using var db = await factory.CreateDbContextAsync(ct);
        var position = 0;
        foreach (var (id, version) in items)
        {
            var entity = await db.SiteNavItems.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null || entity.Version != version)
            {
                results.Add(new BulkItemResult(id, false, "導覽項目已被其他分頁更新，請重新載入。"));
                continue;
            }
            entity.Position = position++;
            entity.Version = Guid.NewGuid();
            results.Add(new BulkItemResult(id, true, ""));
        }
        await ContentRules.SaveAsync(db, ct);
        return results;
    }

    /// <summary>公開導覽：自訂項目（已啟用）或預設的已發布頁面清單。</summary>
    public async Task<List<PageNavItem>> PublicAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        var pages = await db.Contents.AsNoTracking()
            .Where(x => x.Kind == ContentKind.Page && x.Status == ContentStatus.Published && !x.IsPrivate)
            .Select(x => new PageRow(x.Id, x.Title, x.Slug, x.ParentId, x.MenuOrder))
            .ToListAsync(ct);
        if (settings?.CustomNavigationEnabled != true)
        {
            return [.. pages.OrderBy(x => x.MenuOrder).ThenBy(x => x.Title).ThenBy(x => x.Id)
                .Select(x => new PageNavItem(x.Id, x.Title, x.Slug, x.ParentId))];
        }

        var byId = pages.ToDictionary(x => x.Id);
        var items = await db.SiteNavItems.AsNoTracking().Where(x => x.IsVisible)
            .OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync(ct);
        var categorySlugs = await db.Categories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
        var tagSlugs = await db.Tags.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Slug, ct);
        var entries = new List<PageNavItem>(items.Count);
        foreach (var item in items)
        {
            // 每種目標解析成公開網址；目標已不存在時略過，不顯示死連結。
            string? href;
            var external = false;
            if (item.ContentId is { } contentId)
            {
                if (!byId.TryGetValue(contentId, out var page)) continue;
                href = PagePath(page, byId);
            }
            else if (item.CategoryId is { } categoryId && categorySlugs.TryGetValue(categoryId, out var categorySlug))
                href = "/category/" + Uri.EscapeDataString(categorySlug);
            else if (item.TagId is { } tagId && tagSlugs.TryGetValue(tagId, out var tagSlug))
                href = "/tag/" + Uri.EscapeDataString(tagSlug);
            else if (item.ToArchives)
                href = "/archives";
            else if (item.AuthorSlug.Length > 0)
                href = "/author/" + Uri.EscapeDataString(item.AuthorSlug);
            else if (ContentRules.IsHttpUrl(item.Url))
            {
                href = item.Url;
                external = true;
            }
            else continue;
            // 上層未顯示（隱藏或目標失效）時子項目提升為最上層。
            var parent = items.Any(x => x.Id == item.ParentId) ? item.ParentId : null;
            entries.Add(new PageNavItem(item.Id, item.Title, "", parent, href, external) { IsFooter = item.Location == 1 });
        }
        // 子項目若因上層失效而提升，其自身仍可能帶著失效的上層識別；統一以現有項目為準。
        var visibleIds = entries.Select(x => x.Id).ToHashSet();
        return [.. entries.Select(x => x.ParentId is { } owner && !visibleIds.Contains(owner) ? x with { ParentId = null } : x)];
    }

    /// <summary>頁面的階層網址；祖先缺漏時只帶自身（與公開路由一致）。</summary>
    private static string PagePath(PageRow page, Dictionary<Guid, PageRow> byId)
    {
        var parts = new List<string>();
        Guid? cursor = page.Id;
        var guard = 0;
        while (cursor is { } current && byId.TryGetValue(current, out var node) && ++guard <= 10)
        {
            parts.Insert(0, node.Slug);
            cursor = node.ParentId;
        }
        return "/pages/" + string.Join("/", parts.Select(Uri.EscapeDataString));
    }

    private sealed record PageRow(Guid Id, string Title, string Slug, Guid? ParentId, int MenuOrder = 0);

    public sealed class NavItemInput
    {
        public Guid? Id { get; set; }
        public string Title { get; set; } = "";
        public Guid? ContentId { get; set; }
        public string? Url { get; set; }
        public bool IsVisible { get; set; } = true;
        /// <summary>上層導覽項目；最上層為 null。</summary>
        public Guid? ParentId { get; set; }
        /// <summary>顯示位置：0 = 頁首、1 = 頁尾。</summary>
        public int Location { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? TagId { get; set; }
        public bool ToArchives { get; set; }
        public string? AuthorSlug { get; set; }
        public Guid Version { get; set; }
    }
}
