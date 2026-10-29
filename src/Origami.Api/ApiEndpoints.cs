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

        group.MapGet("/search", async (string? q, int? limit, int? page, bool? includePages, PublicSearchService search, CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.BadRequest(new { message = "請輸入搜尋文字。" });
            if (query.Length > Limits.SearchQueryMax) return Results.BadRequest(new { message = "搜尋文字過長。" });
            if (page is < 1) return Results.BadRequest(new { message = "參數 page 無效。" });
            // 缺少嵌入供應商時關鍵字比對仍可運作，僅語意搜尋這一環降級。
            return Results.Ok(await search.SearchAsync(query, limit ?? Limits.PageSize, page ?? 1, ct, includePages == true));
        });

        group.MapGet("/settings", async (SettingsService settings, ContentService content, MediaService media, CancellationToken ct) =>
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
            string? taxonomy = http.Request.Query["taxonomy"].Count > 0 ? http.Request.Query["taxonomy"].ToString() : null;
            string? term = http.Request.Query["term"].Count > 0 ? http.Request.Query["term"].ToString() : null;
            var result = await content.ListByTypeAsync(slug, page ?? 1, pageSize ?? Limits.PageSize, ct, taxonomy, term);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/types/{slug}/taxonomies/{taxonomySlug}/terms/{termSlug}", async (
            string slug, string taxonomySlug, string termSlug, ContentService content, CancellationToken ct) =>
            await content.GetPublicCustomTermAsync(slug, taxonomySlug, termSlug, ct) is { } term
                ? Results.Ok(term)
                : Results.NotFound());

        group.MapGet("/types/{slug}/contents/{contentSlug}", async (string slug, string contentSlug,
            ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedByTypeAsync(slug, contentSlug, ct)));

        group.MapGet("/posts/by-id/{id:guid}", async (Guid id, ContentService content, CancellationToken ct) =>
            PublishedOrNotFound(await content.GetPublishedPostByIdAsync(id, ct)));

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
            string? author = http.Request.Query["author"].Count > 0 ? http.Request.Query["author"].ToString() : null;
            string? typeSlug = http.Request.Query["type"].Count > 0 ? http.Request.Query["type"].ToString() : null;
            string? taxonomy = http.Request.Query["taxonomy"].Count > 0 ? http.Request.Query["taxonomy"].ToString() : null;
            string? term = http.Request.Query["term"].Count > 0 ? http.Request.Query["term"].ToString() : null;
            string? search = http.Request.Query["q"].Count > 0 ? http.Request.Query["q"].ToString() : null;
            // 不複用 BFF 完整呈現方法，避免保護文章的正文經匿名 JSON feed 外洩。
            return Results.Ok(await content.FeedAsync(count, category, tag, ct, author, typeSlug, taxonomy, term, search));
        });

        group.MapGet("/feed/comments", async (CommentService comments, SettingsService settings, CancellationToken ct) =>
        {
            var configured = await settings.GetAsync(ct);
            var count = Math.Clamp(configured.FeedItems ?? Limits.FeedItems, 1, Limits.MaxFeedItems);
            return Results.Ok(await comments.FeedCommentsAsync(count, ct));
        });

        // OpenAPI v3 規格文件與端點探索索引。
        group.MapGet("/openapi.json", (HttpContext http) => Results.Ok(BuildOpenApiDocument(http)));
        app.MapGet("/openapi/v1.json", (HttpContext http) => Results.Ok(BuildOpenApiDocument(http))).AllowAnonymous();
    }

    private static object BuildOpenApiDocument(HttpContext http)
    {
        var serverUrl = $"{http.Request.Scheme}://{http.Request.Host.Value}";
        return new
        {
            openapi = "3.1.0",
            info = new
            {
                title = "Origami Publishing CMS API",
                version = "v1",
                description = "Origami API-first 雙層架構契約：提供公開唯讀端點（/api/v1）與受保護的管理端點（/api/v1/admin，支援 BFF 與個人 API 憑證 PAT）。"
            },
            servers = new[] { new { url = serverUrl } },
            components = new
            {
                securitySchemes = new
                {
                    BearerAuth = new
                    {
                        type = "http",
                        scheme = "bearer",
                        description = "個人 API 憑證（PAT）或 BFF 服務通行證。"
                    }
                }
            },
            paths = new Dictionary<string, object>
            {
                ["/api/v1/posts"] = new { get = new { summary = "列出已發布文章（支援分頁、分類、標籤、作者、年月篩選）", tags = new[] { "Public" } } },
                ["/api/v1/posts/{slug}"] = new { get = new { summary = "依代稱取得單篇已發布文章", tags = new[] { "Public" } } },
                ["/api/v1/pages/{slug}"] = new { get = new { summary = "依代稱取得已發布獨立頁面", tags = new[] { "Public" } } },
                ["/api/v1/types"] = new { get = new { summary = "列出公開自訂內容類型（CPT）", tags = new[] { "Public" } } },
                ["/api/v1/types/{slug}/contents"] = new { get = new { summary = "列出指定自訂內容類型的已發布內容（支援自訂分類法與詞彙篩選）", tags = new[] { "Public" } } },
                ["/api/v1/types/{slug}/contents/{contentSlug}"] = new { get = new { summary = "取得單篇自訂內容類型詳情", tags = new[] { "Public" } } },
                ["/api/v1/search"] = new { get = new { summary = "混合式關鍵字與語意搜尋（type-aware，支援 includePages）", tags = new[] { "Public" } } },
                ["/api/v1/settings"] = new { get = new { summary = "取得站點公開設定、版型、永久連結與時區格式", tags = new[] { "Public" } } },
                ["/api/v1/feed"] = new { get = new { summary = "取得訂閱內容項目（支援分類、標籤、作者、搜尋、CPT 與自訂詞彙篩選）", tags = new[] { "Public" } } },
                ["/api/v1/feed/comments"] = new { get = new { summary = "取得最新已核可評論訂閱項目", tags = new[] { "Public" } } },
                ["/api/v1/sitemap"] = new { get = new { summary = "取得全站公開 Sitemap 項目", tags = new[] { "Public" } } },
                ["/api/v1/admin/contents"] = new
                {
                    get = new { summary = "後台列出內容（支援狀態、關鍵字、作者、分類、標籤與日期區間篩選）", tags = new[] { "Admin" } },
                    post = new { summary = "建立新內容（草稿、待審或直接發布）", tags = new[] { "Admin" } }
                },
                ["/api/v1/admin/contents/{id}"] = new
                {
                    get = new { summary = "讀取內容完整編輯模型", tags = new[] { "Admin" } },
                    put = new { summary = "更新內容並建立完整修訂快照", tags = new[] { "Admin" } },
                    delete = new { summary = "移入回收桶或永久刪除內容", tags = new[] { "Admin" } }
                },
                ["/api/v1/admin/contents/bulk"] = new { post = new { summary = "批次操作內容（回收桶、還原、刪除、改作者、加分類／標籤、開關評論）", tags = new[] { "Admin" } } },
                ["/api/v1/admin/contents/{id}/revisions"] = new { get = new { summary = "列出內容修訂版本", tags = new[] { "Admin" } } },
                ["/api/v1/admin/revisions/{id}"] = new { get = new { summary = "取得修訂版本完整快照（含 SEO、分類、自訂欄位等）", tags = new[] { "Admin" } } },
                ["/api/v1/admin/webhooks/deliveries"] = new { get = new { summary = "查詢持久化 Webhook 投遞紀錄與狀態", tags = new[] { "Admin" } } },
                ["/api/v1/admin/webhooks/deliveries/{id}/retry"] = new { post = new { summary = "手動重送指定的 Webhook 投遞", tags = new[] { "Admin" } } },
                ["/api/v1/admin/site-health"] = new { get = new { summary = "取得管理後台 Site Health 完整系統健康檢查報告", tags = new[] { "Admin" } } },
                ["/api/v1/admin/transfer/export-wxr"] = new { get = new { summary = "匯出站點內容為 WordPress WXR 1.2 XML", tags = new[] { "Admin" } } },
                ["/api/v1/admin/profile/personal-data"] = new
                {
                    get = new { summary = "匯出目前帳號的個人資料套件（GDPR）", tags = new[] { "Privacy" } },
                    delete = new { summary = "執行帳號個人資料抹除與內容匿名化", tags = new[] { "Privacy" } }
                }
            }
        };
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
        public Guid? PrivacyPageContentId { get; init; }
        public string PrivacyPageSlug { get; init; } = "";
        public string PrivacyPageTitle { get; init; } = "";
        public string PermalinkScheme { get; init; } = PermalinkRules.PostName;
        public string TimeZoneId { get; init; } = "UTC";
        public string DateFormat { get; init; } = "yyyy.MM.dd";
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
