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
            .Select(x => new NavItemView(x.Id, x.Title, x.ContentId, x.Url, x.IsVisible, x.Position, x.Version))
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
        else
        {
            if (url.Length == 0) throw new CmsException("請選擇站內頁面或填寫外部連結。");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
                throw new CmsException("外部連結必須是 http 或 https 的絕對網址。");
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
        entity.Url = url;
        entity.IsVisible = input.IsVisible;
        entity.ContentId = input.ContentId;
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
        return new(entity.Id, entity.Title, entity.ContentId, entity.Url, entity.IsVisible, entity.Position, entity.Version);
    }

    public async Task DeleteAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Editor, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.SiteNavItems.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("導覽項目不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
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
        var entries = new List<PageNavItem>(items.Count);
        foreach (var item in items)
        {
            if (item.ContentId is { } contentId)
            {
                // 目標頁面已下架或轉為私密時略過，不顯示死連結。
                if (!byId.TryGetValue(contentId, out var page)) continue;
                entries.Add(new PageNavItem(item.Id, item.Title, page.Slug, null, PagePath(page, byId), false));
            }
            else if (item.Url.Length > 0)
            {
                entries.Add(new PageNavItem(item.Id, item.Title, "", null, item.Url, true));
            }
        }
        return entries;
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
        public Guid Version { get; set; }
    }
}
