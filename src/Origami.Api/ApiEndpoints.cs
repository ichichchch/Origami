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
            // 作者彙整篩選：以署名代稱（顯示名稱的正規化 slug）過濾已發布文章。
            string? author = http.Request.Query["author"].Count > 0 ? http.Request.Query["author"].ToString() : null;
            if (!TryQueryInt(http, "year", null, minimum: 1, maximum: 9999, out var year, out error)) return error;
            if (!TryQueryInt(http, "month", null, minimum: 1, maximum: 12, out var month, out error)) return error;
            return Results.Ok(await content.ListPublishedAsync(page ?? 1, pageSize ?? Limits.PageSize, ct, category, tag, year, month, author));
        });

        group.MapGet("/posts/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Post, slug, ct)));

        group.MapGet("/pages/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Page, slug, ct)));

        group.MapGet("/search", async (string? q, int? limit, int? page, PublicSearchService search, CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.BadRequest(new { message = "請輸入搜尋文字。" });
            if (query.Length > Limits.SearchQueryMax) return Results.BadRequest(new { message = "搜尋文字過長。" });
            if (page is < 1) return Results.BadRequest(new { message = "參數 page 無效。" });
            // 缺少嵌入供應商時關鍵字比對仍可運作，僅語意搜尋這一環降級。
            return Results.Ok(await search.SearchAsync(query, limit ?? Limits.PageSize, page ?? 1, ct));
        });

        group.MapGet("/settings", async (SettingsService settings, MediaService media, CancellationToken ct) =>
        {
            var value = await settings.GetAsync(ct);
            // 頁首標誌以檔名呈現；媒體網址由網站主機的 /media 提供。
            var logoFileName = await media.GetSiteLogoFileNameAsync(ct);
            return Results.Ok(new PublicSiteSettings(value.Name, value.Description, value.PostsPerPage,
                value.FeedItems, value.FeedShowFullText, value.AuthorDisplayName, value.HomePageContentId, value.SiteIconMediaId, value.LogoMediaId)
            { LogoFileName = logoFileName ?? "", CommentsEnabled = value.CommentsEnabled,
                CommentCloseDays = value.CommentCloseDays,
                ThemePreset = value.ThemePreset, AccentColor = value.AccentColor, ReadingWidth = value.ReadingWidth,
                ShowByline = value.ShowByline, ShowCover = value.ShowCover, CardStyle = value.CardStyle,
                ColorScheme = value.ColorScheme, FooterText = value.FooterText,
                SocialGithubUrl = value.SocialGithubUrl, SocialXUrl = value.SocialXUrl, SocialMastodonUrl = value.SocialMastodonUrl,
                MaintenanceMode = value.MaintenanceMode, MaintenanceMessage = value.MaintenanceMessage });
        });

        group.MapGet("/navigation", async (NavigationService navigation, CancellationToken ct) =>
            Results.Ok(await navigation.PublicAsync(ct)));

        // 已設定為靜態首頁的頁面：以內容識別取得已發布頁面。
        group.MapGet("/contents/{id}/published", async (Guid id, ContentService content, CancellationToken ct) =>
            await content.GetPublishedPageAsync(id, ct) is { } page ? Results.Ok(page) : Results.NotFound());

        // 公開圖片寬度表：前台 srcset 渲染所需的變體資訊。
        group.MapGet("/media/widths", async (MediaService media, CancellationToken ct) =>
            Results.Ok(await media.PublicImageWidthsAsync(ct)));

        // 已核可評論的公開讀取；與內容詳情同一公開性規則。
        group.MapGet("/contents/{id}/comments", async (Guid id, CommentService comments, CancellationToken ct) =>
            Results.Ok(await comments.ListPublicAsync(id, ct)));

        group.MapGet("/contents/{id}/neighbors", async (Guid id, ContentService content, CancellationToken ct) =>
            Results.Ok(await content.NeighborsAsync(id, ct)));

        group.MapGet("/contents/{id}/related", async (Guid id, SemanticSearchService semantic, CancellationToken ct) =>
            Results.Ok(await semantic.RelatedAsync(id, ct: ct)));

        group.MapGet("/archives", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.ArchiveMonthsAsync(ct)));

        // 自訂內容類型的公開區塊：類型清單、該類型的已發布內容與單篇內容。
        group.MapGet("/types", async (ContentTypeService types, CancellationToken ct) =>
            Results.Ok(await types.ListPublicAsync(ct)));

        group.MapGet("/types/{slug}/contents", async (string slug, ContentService content, HttpContext http,
            CancellationToken ct) =>
        {
            if (!TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
            if (!TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
            var result = await content.ListByTypeAsync(slug, page ?? 1, pageSize ?? Limits.PageSize, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/types/{slug}/contents/{contentSlug}", async (string slug, string contentSlug,
            ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedByTypeAsync(slug, contentSlug, ct)));

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

        group.MapGet("/authors/{slug}", async (string slug, ContentService content, CancellationToken ct) =>
            await content.GetPublicAuthorAsync(slug, ct) is { } author ? Results.Ok(author) : Results.NotFound());

        // 草稿分享預覽：內容本體，加上已發布時應轉址前往的公開位置。
        group.MapGet("/preview/{token}", async (Guid token, ContentService content, CancellationToken ct) =>
        {
            var preview = await content.GetPreviewAsync(token, ct);
            var published = await content.ResolvePublishedPreviewAsync(token, ct);
            // 已發布後草稿投影為 null；此時仍須以轉址資訊回應，不得簡化成 404。
            if (preview is null && published is null) return Results.NotFound();
            return Results.Ok(new ApiPreview(preview, published?.Kind.ToString(), published?.Slug));
        });

        group.MapGet("/sitemap", async (ContentService content, CancellationToken ct) =>
            Results.Ok(await content.SitemapAsync(ct)));

        group.MapGet("/feed", async (ContentService content, SettingsService settings, HttpContext http, CancellationToken ct) =>
        {
            var configured = await settings.GetAsync(ct);
            var count = Math.Clamp(configured.FeedItems ?? Limits.FeedItems, 1, Limits.MaxFeedItems);
            string? category = http.Request.Query["category"].Count > 0 ? http.Request.Query["category"].ToString() : null;
            string? tag = http.Request.Query["tag"].Count > 0 ? http.Request.Query["tag"].ToString() : null;
            // 不複用 BFF 完整呈現方法，避免保護文章的正文經匿名 JSON feed 外洩。
            return Results.Ok(await content.FeedAsync(count, category, tag, ct));
        });
    }

    public sealed record PublicSiteSettings(string Name, string Description, int? PostsPerPage, int? FeedItems,
        bool? FeedShowFullText, string AuthorDisplayName, Guid? HomePageContentId, Guid? SiteIconMediaId, Guid? LogoMediaId = null)
    {
        /// <summary>標誌的儲存檔名；未設定時為空字串，頁首僅顯示站名。</summary>
        public string LogoFileName { get; init; } = "";
        /// <summary>是否開放訪客評論。</summary>
        public bool CommentsEnabled { get; init; }
        /// <summary>文章發布超過指定天數後自動關閉評論。</summary>
        public int CommentCloseDays { get; init; }
        /// <summary>受控版型預設名稱。</summary>
        public string ThemePreset { get; init; } = "reading";
        /// <summary>版型強調色（十六進位色碼）。</summary>
        public string AccentColor { get; init; } = "#2f6f4e";
        /// <summary>正文欄寬（字元）。</summary>
        public int ReadingWidth { get; init; } = 68;
        /// <summary>是否顯示文章署名。</summary>
        public bool ShowByline { get; init; } = true;
        /// <summary>是否顯示封面圖。</summary>
        public bool ShowCover { get; init; } = true;
        /// <summary>文章卡片樣式：list 或 grid。</summary>
        public string CardStyle { get; init; } = "list";
        /// <summary>色彩模式：auto、light 或 dark。</summary>
        public string ColorScheme { get; init; } = "auto";
        /// <summary>自訂頁尾文字。</summary>
        public string FooterText { get; init; } = "";
        public string SocialGithubUrl { get; init; } = "";
        public string SocialXUrl { get; init; } = "";
        public string SocialMastodonUrl { get; init; } = "";
        public bool MaintenanceMode { get; init; }
        public string MaintenanceMessage { get; init; } = "";
    }

    /// <summary>草稿預覽，或內容已發布時的永久轉址目標；兩者至多其一存在。</summary>
    public sealed record ApiPreview(PublicContent? Content, string? PublishedKind, string? PublishedSlug);

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
