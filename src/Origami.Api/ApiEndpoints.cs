namespace Origami.Api;

/// <summary>
/// 供原生與行動用戶端使用的唯讀 JSON API v1。與 MCP 唯讀介面一致：
/// 僅提供已發布內容，草稿、受保護與私密內容一律不回傳。
/// </summary>
public static class ApiEndpoints
{
    /// <summary>註冊公開唯讀端點；各端點的處理邏輯各自成方法，避免單一方法累積分支與巢狀 lambda。</summary>
    public static void MapApiV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1");

        group.MapGet("/posts", ListPostsAsync);
        group.MapGet("/posts/{slug}", GetPostAsync);
        group.MapGet("/pages/{slug}", GetPageAsync);
        group.MapGet("/search", SearchAsync);
        group.MapGet("/settings", GetSettingsAsync);
        group.MapGet("/navigation", GetNavigationAsync);
        // 已設定為靜態首頁的頁面：以內容識別取得已發布頁面。
        group.MapGet("/contents/{id}/published", GetPublishedPageAsync);
        // 公開圖片寬度表：前台 srcset 渲染所需的變體資訊。
        group.MapGet("/media/widths", GetMediaWidthsAsync);
        // 已核可評論的公開讀取；與內容詳情同一公開性規則。
        group.MapGet("/contents/{id}/comments", ListPublicCommentsAsync);
        group.MapGet("/contents/{id}/neighbors", GetNeighborsAsync);
        group.MapGet("/contents/{id}/related", GetRelatedAsync);
        group.MapGet("/archives", GetArchivesAsync);
        // 自訂內容類型的公開區塊：類型清單、該類型的已發布內容與單篇內容。
        group.MapGet("/types", ListPublicTypesAsync);
        group.MapGet("/types/{slug}/contents", ListTypeContentsAsync);
        group.MapGet("/types/{slug}/taxonomies/{taxonomySlug}/terms/{termSlug}", GetTypeTermAsync);
        group.MapGet("/types/{slug}/contents/{contentSlug}", GetTypeContentAsync);
        group.MapGet("/posts/by-id/{id:guid}", GetPostByIdAsync);
        group.MapGet("/categories/popular", GetPopularCategoriesAsync);
        group.MapGet("/taxonomies/{kind}/{slug}", GetTaxonomyAsync);
        group.MapGet("/redirects/{slug}", GetRedirectAsync);
        group.MapGet("/authors/{slug}", GetAuthorAsync);
        // 草稿分享預覽：內容本體，加上已發布時應轉址前往的公開位置。
        group.MapGet("/preview/{token}", GetPreviewAsync);
        group.MapGet("/sitemap", GetSitemapAsync);
        group.MapGet("/feed", GetFeedAsync);
        group.MapGet("/feed/comments", GetCommentsFeedAsync);
        // OpenAPI v3 規格文件與端點探索索引。
        group.MapGet("/openapi.json", GetOpenApiDocument);
        app.MapGet("/openapi/v1.json", GetOpenApiDocument).AllowAnonymous();
    }

    /// <summary>已發布文章列表：支援分類、標籤、作者與年月篩選。</summary>
    private static async Task<IResult> ListPostsAsync(ContentService content, HttpContext http, CancellationToken ct)
    {
        if (!TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
        if (!TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
        string? category = QueryValue(http, "category");
        string? tag = QueryValue(http, "tag");
        // 作者彙整篩選：以署名代稱（顯示名稱的正規化 slug）過濾已發布文章。
        string? author = QueryValue(http, "author");
        if (!TryQueryInt(http, "year", null, minimum: 1, maximum: 9999, out var year, out error)) return error;
        if (!TryQueryInt(http, "month", null, minimum: 1, maximum: 12, out var month, out error)) return error;
        return Results.Ok(await content.ListPublishedAsync(page ?? 1, pageSize ?? Limits.PageSize, ct, category, tag, year, month, author));
    }

    /// <summary>單篇已發布文章。</summary>
    private static async Task<IResult> GetPostAsync(string slug, ContentService content, CancellationToken ct) =>
        PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Post, slug, ct));

    /// <summary>單篇已發布獨立頁面。</summary>
    private static async Task<IResult> GetPageAsync(string slug, ContentService content, CancellationToken ct) =>
        PublishedOrNotFound(await content.GetPublishedAsync(ContentKind.Page, slug, ct));

    /// <summary>全站搜尋：缺少嵌入供應商時關鍵字比對仍可運作，僅語意搜尋這一環降級。</summary>
    private static async Task<IResult> SearchAsync(string? q, int? limit, int? page, bool? includePages,
        PublicSearchService search, CancellationToken ct)
    {
        var query = (q ?? "").Trim();
        if (query.Length == 0) return Results.BadRequest(new { message = "請輸入搜尋文字。" });
        if (query.Length > Limits.SearchQueryMax) return Results.BadRequest(new { message = "搜尋文字過長。" });
        if (page is < 1) return Results.BadRequest(new { message = "參數 page 無效。" });
        return Results.Ok(await search.SearchAsync(query, limit ?? Limits.PageSize, page ?? 1, ct, includePages == true));
    }

    /// <summary>站點公開設定、版型與呈現選項。</summary>
    private static async Task<IResult> GetSettingsAsync(SettingsService settings, ContentService content, MediaService media,
        CancellationToken ct)
    {
        var value = await settings.GetAsync(ct);
        // 頁首標誌以檔名呈現；媒體網址由網站主機的 /media 提供。
        var logoFileName = await media.GetSiteLogoFileNameAsync(ct);
        string privacySlug = "", privacyTitle = "";
        if (value.PrivacyPageContentId is { } privId && await content.GetPublishedPageAsync(privId, ct) is { } privPage)
        {
            privacySlug = privPage.Slug;
            privacyTitle = privPage.Title;
        }
        return Results.Ok(new PublicSiteSettings(value.Name, value.Description, value.PostsPerPage,
            value.FeedItems, value.FeedShowFullText, value.AuthorDisplayName, value.HomePageContentId, value.SiteIconMediaId, value.LogoMediaId)
        { LogoFileName = logoFileName ?? "", CommentsEnabled = value.CommentsEnabled,
            CommentCloseDays = value.CommentCloseDays,
            ThemePreset = value.ThemePreset, AccentColor = value.AccentColor, ReadingWidth = value.ReadingWidth,
            ShowByline = value.ShowByline, ShowCover = value.ShowCover, CardStyle = value.CardStyle,
            ColorScheme = value.ColorScheme, FooterText = value.FooterText,
            SocialGithubUrl = value.SocialGithubUrl, SocialXUrl = value.SocialXUrl, SocialMastodonUrl = value.SocialMastodonUrl,
            MaintenanceMode = value.MaintenanceMode, MaintenanceMessage = value.MaintenanceMessage,
            PrivacyPageContentId = value.PrivacyPageContentId, PrivacyPageSlug = privacySlug, PrivacyPageTitle = privacyTitle,
            PermalinkScheme = value.PermalinkScheme, TimeZoneId = value.TimeZoneId, DateFormat = value.DateFormat, TimeFormat = value.TimeFormat });
    }

    /// <summary>前台頁首呈現所需的公開導覽選單。</summary>
    private static async Task<IResult> GetNavigationAsync(NavigationService navigation, CancellationToken ct) =>
        Results.Ok(await navigation.PublicAsync(ct));

    /// <summary>以內容識別取得已發布頁面，供靜態首頁渲染。</summary>
    private static async Task<IResult> GetPublishedPageAsync(Guid id, ContentService content, CancellationToken ct) =>
        await content.GetPublishedPageAsync(id, ct) is { } page ? Results.Ok(page) : Results.NotFound();

    /// <summary>前台響應式圖片渲染所需的公開寬度設定。</summary>
    private static async Task<IResult> GetMediaWidthsAsync(MediaService media, CancellationToken ct) =>
        Results.Ok(await media.PublicImageWidthsAsync(ct));

    /// <summary>指定內容下所有已核可的公開評論。</summary>
    private static async Task<IResult> ListPublicCommentsAsync(Guid id, CommentService comments, CancellationToken ct) =>
        Results.Ok(await comments.ListPublicAsync(id, ct));

    /// <summary>指定文章的上一篇與下一篇相鄰文章。</summary>
    private static async Task<IResult> GetNeighborsAsync(Guid id, ContentService content, CancellationToken ct) =>
        Results.Ok(await content.NeighborsAsync(id, ct));

    /// <summary>與指定內容語意相關的推薦文章清單。</summary>
    private static async Task<IResult> GetRelatedAsync(Guid id, SemanticSearchService semantic, CancellationToken ct) =>
        Results.Ok(await semantic.RelatedAsync(id, ct: ct));

    /// <summary>已發布文章的年月彙整與數量統計。</summary>
    private static async Task<IResult> GetArchivesAsync(ContentService content, CancellationToken ct) =>
        Results.Ok(await content.ArchiveMonthsAsync(ct));

    /// <summary>所有對外公開的自訂內容類型。</summary>
    private static async Task<IResult> ListPublicTypesAsync(ContentTypeService types, CancellationToken ct) =>
        Results.Ok(await types.ListPublicAsync(ct));

    /// <summary>指定自訂內容類型的已發布內容，支援自訂分類法與詞彙篩選。</summary>
    private static async Task<IResult> ListTypeContentsAsync(string slug, ContentService content, HttpContext http,
        CancellationToken ct)
    {
        if (!TryQueryInt(http, "page", 1, minimum: 1, maximum: int.MaxValue, out var page, out var error)) return error;
        if (!TryQueryInt(http, "pageSize", Limits.PageSize, minimum: 1, maximum: Limits.MaxPageSize, out var pageSize, out error)) return error;
        string? taxonomy = QueryValue(http, "taxonomy");
        string? term = QueryValue(http, "term");
        var result = await content.ListByTypeAsync(slug, page ?? 1, pageSize ?? Limits.PageSize, ct, taxonomy, term);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    /// <summary>指定自訂內容類型與分類法下的公開詞彙詳情。</summary>
    private static async Task<IResult> GetTypeTermAsync(string slug, string taxonomySlug, string termSlug,
        ContentService content, CancellationToken ct) =>
        await content.GetPublicCustomTermAsync(slug, taxonomySlug, termSlug, ct) is { } term
            ? Results.Ok(term)
            : Results.NotFound();

    /// <summary>指定自訂內容類型的單篇已發布內容。</summary>
    private static async Task<IResult> GetTypeContentAsync(string slug, string contentSlug, ContentService content,
        CancellationToken ct) =>
        PublishedOrNotFound(await content.GetPublishedByTypeAsync(slug, contentSlug, ct));

    /// <summary>依內容識別取得單篇已發布文章。</summary>
    private static async Task<IResult> GetPostByIdAsync(Guid id, ContentService content, CancellationToken ct) =>
        PublishedOrNotFound(await content.GetPublishedPostByIdAsync(id, ct));

    /// <summary>文章數量最多的熱門分類清單。</summary>
    private static async Task<IResult> GetPopularCategoriesAsync(ContentService content, CancellationToken ct) =>
        Results.Ok(await content.PopularCategoriesAsync(ct));

    /// <summary>依分類法類型與代稱取得公開分類法詳情。</summary>
    private static async Task<IResult> GetTaxonomyAsync(string kind, string slug, ContentService content, CancellationToken ct)
    {
        if (!Enum.TryParse<TaxonomyKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return Results.BadRequest(new { message = "參數 kind 無效（category 或 tag）。" });
        return await content.GetPublicTaxonomyAsync(parsed, slug, ct) is { } view
            ? Results.Ok(view)
            : Results.NotFound();
    }

    /// <summary>舊內容代稱對應的永久轉址目標。</summary>
    private static async Task<IResult> GetRedirectAsync(string slug, ContentService content, CancellationToken ct) =>
        await content.RedirectTargetAsync(slug, ct) is { } target ? Results.Ok(target) : Results.NotFound();

    /// <summary>依作者代稱取得公開作者基本資訊。</summary>
    private static async Task<IResult> GetAuthorAsync(string slug, ContentService content, CancellationToken ct) =>
        await content.GetPublicAuthorAsync(slug, ct) is { } author ? Results.Ok(author) : Results.NotFound();

    /// <summary>草稿分享預覽：內容本體，加上已發布時應轉址前往的公開位置。</summary>
    private static async Task<IResult> GetPreviewAsync(Guid token, ContentService content, CancellationToken ct)
    {
        var preview = await content.GetPreviewAsync(token, ct);
        var published = await content.ResolvePublishedPreviewAsync(token, ct);
        // 已發布後草稿投影為 null；此時仍須以轉址資訊回應，不得簡化成 404。
        if (preview is null && published is null) return Results.NotFound();
        return Results.Ok(new ApiPreview(preview, published?.Kind.ToString(), published?.Slug));
    }

    /// <summary>全站已發布內容的公開網站地圖項目。</summary>
    private static async Task<IResult> GetSitemapAsync(ContentService content, CancellationToken ct) =>
        Results.Ok(await content.SitemapAsync(ct));

    /// <summary>最新訂閱內容項目，支援分類、標籤、作者、關鍵字、自訂類型與詞彙篩選。</summary>
    private static async Task<IResult> GetFeedAsync(ContentService content, SettingsService settings, HttpContext http,
        CancellationToken ct)
    {
        var configured = await settings.GetAsync(ct);
        var count = Math.Clamp(configured.FeedItems ?? Limits.FeedItems, 1, Limits.MaxFeedItems);
        string? category = QueryValue(http, "category");
        string? tag = QueryValue(http, "tag");
        string? author = QueryValue(http, "author");
        string? typeSlug = QueryValue(http, "type");
        string? taxonomy = QueryValue(http, "taxonomy");
        string? term = QueryValue(http, "term");
        string? search = QueryValue(http, "q");
        // 不複用 BFF 完整呈現方法，避免保護文章的正文經匿名 JSON feed 外洩。
        return Results.Ok(await content.FeedAsync(count, category, tag, ct, author, typeSlug, taxonomy, term, search));
    }

    /// <summary>全站最新已核可評論的訂閱項目。</summary>
    private static async Task<IResult> GetCommentsFeedAsync(CommentService comments, SettingsService settings,
        CancellationToken ct)
    {
        var configured = await settings.GetAsync(ct);
        var count = Math.Clamp(configured.FeedItems ?? Limits.FeedItems, 1, Limits.MaxFeedItems);
        return Results.Ok(await comments.FeedCommentsAsync(count, ct));
    }

    /// <summary>由實際路由表產生的 OpenAPI v3.1 規格文件。</summary>
    private static IResult GetOpenApiDocument(HttpContext http) => Results.Ok(BuildOpenApiDocument(http));

    private static object BuildOpenApiDocument(HttpContext http) => OpenApiDocumentBuilder.Build(http);

    /// <summary>讀取查詢字串；未提供該參數時回傳 null（代表「無偏好」）。</summary>
    private static string? QueryValue(HttpContext http, string key) =>
        http.Request.Query[key].Count > 0 ? http.Request.Query[key].ToString() : null;

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
        /// <summary>站點的 GitHub 社群連結。</summary>
        public string SocialGithubUrl { get; init; } = "";
        /// <summary>站點的 X（Twitter）社群連結。</summary>
        public string SocialXUrl { get; init; } = "";
        /// <summary>站點的 Mastodon 社群連結。</summary>
        public string SocialMastodonUrl { get; init; } = "";
        /// <summary>是否啟用維護模式。</summary>
        public bool MaintenanceMode { get; init; }
        /// <summary>維護模式下顯示給訪客的提示訊息。</summary>
        public string MaintenanceMessage { get; init; } = "";
        /// <summary>隱私權頁面的內容識別碼。</summary>
        public Guid? PrivacyPageContentId { get; init; }
        /// <summary>隱私權頁面的代稱（slug）。</summary>
        public string PrivacyPageSlug { get; init; } = "";
        /// <summary>隱私權頁面的標題。</summary>
        public string PrivacyPageTitle { get; init; } = "";
        /// <summary>文章永久連結規則。</summary>
        public string PermalinkScheme { get; init; } = PermalinkRules.PostName;
        /// <summary>站點時區識別碼（IANA）。</summary>
        public string TimeZoneId { get; init; } = "UTC";
        /// <summary>日期顯示格式。</summary>
        public string DateFormat { get; init; } = "yyyy.MM.dd";
        /// <summary>時間顯示格式。</summary>
        public string TimeFormat { get; init; } = "HH:mm";
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
