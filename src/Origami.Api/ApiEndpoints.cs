namespace Origami.Api;

/// <summary>
/// 供原生與行動用戶端使用的唯讀 JSON API v1。與 MCP 唯讀介面一致：
/// 僅提供已發布內容，草稿、受保護與私密內容一律不回傳。
/// </summary>
public static class ApiEndpoints
{
    public static void MapApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/posts", async (ContentService content, HttpContext http, CancellationToken ct) =>
        {
            if (!TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            string? category = http.Request.Query["category"].Count > 0 ? http.Request.Query["category"].ToString() : null;
            string? tag = http.Request.Query["tag"].Count > 0 ? http.Request.Query["tag"].ToString() : null;
            if (!TryQueryInt(http, "year", null, minimum: 1, maximum: 9999, out var year, out error)) return error;
            if (!TryQueryInt(http, "month", null, minimum: 1, maximum: 12, out var month, out error)) return error;
            return Results.Ok(await content.ListPublishedAsync(page ?? 1, pageSize ?? Limits.PageSize, ct, category, tag, year, month));
        });

        group.MapGet("/posts/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Post, slug, ct)));

        group.MapGet("/pages/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Page, slug, ct)));

        group.MapGet("/search", async (string? q, int? limit, PublicSearchService search, CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.BadRequest(new { message = "請輸入搜尋文字。" });
            if (query.Length > Limits.SearchQueryMax) return Results.BadRequest(new { message = "搜尋文字過長。" });
            // 缺少嵌入供應商時關鍵字比對仍可運作，僅語意搜尋這一環降級。
            return Results.Ok(await search.SearchAsync(query, limit ?? Limits.PageSize, ct));
        });

        group.MapGet("/settings", async (SettingsService settings, CancellationToken ct) =>
        {
            var value = await settings.GetAsync(ct);
            return Results.Ok(new PublicSiteSettings(value.Name, value.Description, value.PostsPerPage,
                value.FeedItems, value.HomePageContentId, value.SiteIconMediaId));
        });

        group.MapGet("/navigation", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.NavigationAsync(ct)));

        group.MapGet("/contents/{id}/neighbors", async (Guid id, ContentService content, CancellationToken ct) =>
            Results.Ok(await content.NeighborsAsync(id, ct)));

        group.MapGet("/contents/{id}/related", async (Guid id, SemanticSearchService semantic, CancellationToken ct) =>
            Results.Ok(await semantic.RelatedAsync(id, ct: ct)));

        group.MapGet("/archives", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.ArchiveMonthsAsync(ct)));

        group.MapGet("/categories/popular", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.PopularCategoriesAsync(ct)));

        group.MapGet("/taxonomies/{kind}/{slug}", async (string kind, string slug, ContentService content, CancellationToken ct) =>
        {
            if (!Enum.TryParse<TaxonomyKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Results.BadRequest(new { message = "參數 kind 無效（category 或 tag）。" });
            return await content.GetPublicTaxonomyAsync(parsed, slug, ct) is { } view
                ? Results.Ok(view)
                : Results.NotFound();
        });

        group.MapGet("/redirects/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            await content.RedirectTargetAsync(slug, ct) is { } target ? Results.Ok(target) : Results.NotFound());

        // 草稿分享預覽：內容本體，加上已發布時應轉址前往的公開位置。
        group.MapGet("/preview/{token}", async (Guid token, ContentService content, CancellationToken ct) =>
        {
            var preview = await content.GetPreviewAsync(token, ct);
            if (preview is null) return Results.NotFound();
            var published = await content.ResolvePublishedPreviewAsync(token, ct);
            return Results.Ok(new ApiPreview(preview, published?.Kind.ToString(), published?.Slug));
        });

        group.MapGet("/sitemap", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.SitemapAsync(ct)));

        group.MapGet("/feed", async (ContentService content, SettingsService settings, CancellationToken ct) =>
        {
            var configured = await settings.GetAsync(ct);
            var count = Math.Clamp(configured.FeedItems ?? Limits.FeedItems, 1, Limits.MaxFeedItems);
            var page = await content.ListPublishedAsync(1, count, ct);
            var items = new List<ApiFeedItem>();
            foreach (var summary in page.Items)
            {
                var detail = await content.GetPublishedAsync(ContentKind.Post, summary.Slug, ct);
                items.Add(new(summary.Id, summary.Title, summary.Slug, summary.PublishedAt,
                    summary.Excerpt, detail?.Markdown ?? ""));
            }
            return Results.Ok(items);
        });
    }

    public sealed record PublicSiteSettings(string Name, string Description, int? PostsPerPage, int? FeedItems,
        Guid? HomePageContentId, Guid? SiteIconMediaId);

    public sealed record ApiPreview(PublicContent Content, string? PublishedKind, string? PublishedSlug);

    public sealed record ApiFeedItem(Guid Id, string Title, string Slug, DateTimeOffset? PublishedAt,
        string Excerpt, string Markdown);

    /// <summary>受密碼保護與私密內容對機器用戶端不可見，與 MCP 工具的行為一致。</summary>
    private static IResult PublishedOrNotFound(PublicContent? found) =>
        found is { IsProtected: false, IsPrivate: false } ? Results.Ok(found) : Results.NotFound();

    internal static bool TryQueryInt(HttpContext http, string key, int? defaultValue, int minimum, int maximum,
        out int? value, out IResult error)
    {
        // 未提供代表「無偏好」：退回預設值，選用性篩選的預設值仍為 null。
        value = defaultValue;
        error = Results.Empty;
        var raw = http.Request.Query[key];
        if (raw.Count == 0) return true;
        if (!int.TryParse(raw, out var parsed) || parsed < minimum || parsed > maximum)
        {
            error = Results.BadRequest(new { message = $"參數 {key} 無效。" });
            return false;
        }
        value = parsed;
        return true;
    }
}
