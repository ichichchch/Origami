namespace Origami.Web.Services;

/// <summary>
/// 機器端公開端點：robots.txt、sitemap.xml、RSS 2.0 與 Atom 1.0 feed、oEmbed provider。
/// 每次請求都讀取即時的已發布狀態，因此撤回或刪除的內容會立即消失。
/// </summary>
public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (HttpContext http) => Results.Text(
            "User-agent: *\nDisallow: /admin\nDisallow: /account\nDisallow: /preview\n\nSitemap: " + BaseUrl(http) + "/sitemap.xml\n",
            "text/plain", Encoding.UTF8));

        app.MapGet("/sitemap.xml", async (OrigamiClient api, HttpContext http, CancellationToken ct) =>
        {
            var items = await api.GetSitemapAsync(ct) ?? [];
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(ns + "urlset", items.Select(item => new XElement(ns + "url",
                    new XElement(ns + "loc", BaseUrl(http) + item.Path),
                    item.LastModified is { } last ? new XElement(ns + "lastmod", last.UtcDateTime.ToString("yyyy-MM-dd")) : null))));
            return Results.Text(ToXml(document), "application/xml", Encoding.UTF8);
        });

        // RSS 2.0：支援 ?category=、?tag=、?author=、?type=、?taxonomy=&term= 與 ?q= 過濾；不存在的分類、標籤、作者或類型回 404。
        // 設定開啟全文時，完整 HTML 放入 content:encoded，description 維持摘要。
        app.MapGet("/feed", async (OrigamiClient api, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var feed = await TryLoadFeedAsync(api, http, ct);
            if (feed is null) return Results.NotFound();
            var (site, items, titleSuffix) = feed.Value;
            return RenderRss(site, items, titleSuffix, markdown, http, await LoadWidthsIfFullTextAsync(api, site, ct));
        });

        app.MapGet("/t/{typeSlug}/feed", async (string typeSlug, OrigamiClient api, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var feed = await TryLoadTypeFeedAsync(api, typeSlug, null, null, ct);
            if (feed is null) return Results.NotFound();
            var (site, items, titleSuffix) = feed.Value;
            return RenderRss(site, items, titleSuffix, markdown, http, await LoadWidthsIfFullTextAsync(api, site, ct));
        });

        app.MapGet("/t/{typeSlug}/{taxonomySlug}/{termSlug}/feed", async (
            string typeSlug, string taxonomySlug, string termSlug, OrigamiClient api, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var feed = await TryLoadTypeFeedAsync(api, typeSlug, taxonomySlug, termSlug, ct);
            if (feed is null) return Results.NotFound();
            var (site, items, titleSuffix) = feed.Value;
            return RenderRss(site, items, titleSuffix, markdown, http, await LoadWidthsIfFullTextAsync(api, site, ct));
        });

        app.MapGet("/author/{authorSlug}/feed", async (string authorSlug, OrigamiClient api, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var author = await api.GetPublicAuthorAsync(authorSlug, ct);
            if (author is null) return Results.NotFound();
            var site = await api.GetPublicSettingsAsync(ct) ?? PublicSiteSettings.Default;
            var items = await api.GetFeedItemsAsync(author: author.Slug, ct: ct) ?? [];
            return RenderRss(site, items, " · " + author.DisplayName, markdown, http, await LoadWidthsIfFullTextAsync(api, site, ct));
        });

        // 評論專屬 RSS 2.0 Feed（/feed/comments）。
        app.MapGet("/feed/comments", async (OrigamiClient api, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var site = await api.GetPublicSettingsAsync(ct) ?? PublicSiteSettings.Default;
            var comments = await api.GetCommentFeedItemsAsync(ct) ?? [];
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("rss", new XAttribute("version", "2.0"),
                    new XAttribute(XNamespace.Xmlns + "dc", dc),
                    new XElement("channel",
                        new XElement("title", site.Name + " · 最新評論"),
                        new XElement("link", BaseUrl(http) + "/"),
                        new XElement("description", site.Description),
                        comments.Select(c =>
                        {
                            var link = BaseUrl(http) + c.PublicPath + "#comments-title";
                            return new XElement("item",
                                new XElement("title", $"{c.AuthorName} 於〈{c.ContentTitle}〉的評論"),
                                new XElement("link", link),
                                new XElement("guid", new XAttribute("isPermaLink", "false"), "comment-" + c.Id),
                                new XElement("pubDate", c.CreatedAt.UtcDateTime.ToString("R")),
                                new XElement(dc + "creator", c.AuthorName),
                                new XElement("description", markdown.PlainText(c.Markdown)));
                        }))));
            return Results.Text(ToXml(document), "application/rss+xml", Encoding.UTF8);
        });

        // Atom 1.0：與 RSS 同一資料來源，同樣支援分類／標籤／作者／搜尋／CPT 過濾與全文模式。
        app.MapGet("/feed/atom", async (OrigamiClient api, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var feed = await TryLoadFeedAsync(api, http, ct);
            if (feed is null) return Results.NotFound();
            var (site, items, titleSuffix) = feed.Value;
            var fullText = site.FeedShowFullText == true;
            var widths = fullText ? await api.GetPublicImageWidthsAsync(ct) ?? [] : null;
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var updated = items.Count == 0 ? DateTimeOffset.UtcNow : items.Max(i => i.PublishedAt) ?? DateTimeOffset.UtcNow;
            var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(atom + "feed",
                    new XElement(atom + "title", site.Name + titleSuffix),
                    new XElement(atom + "subtitle", site.Description),
                    new XElement(atom + "id", BaseUrl(http) + "/"),
                    new XElement(atom + "link", new XAttribute("href", BaseUrl(http) + "/")),
                    new XElement(atom + "link", new XAttribute("href", BaseUrl(http) + "/feed/atom" + FeedQuery(http)), new XAttribute("rel", "self")),
                    new XElement(atom + "updated", updated.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                    !string.IsNullOrWhiteSpace(site.AuthorDisplayName)
                        ? new XElement(atom + "author", new XElement(atom + "name", site.AuthorDisplayName)) : null,
                    items.Select(item =>
                    {
                        var link = BaseUrl(http) + item.PublicPath;
                        var summary = item.ExcerptIsManual ? item.Excerpt : markdown.Excerpt(item.Excerpt);
                        return new XElement(atom + "entry",
                            new XElement(atom + "title", item.Title),
                            new XElement(atom + "link", new XAttribute("href", link)),
                            new XElement(atom + "id", link),
                            item.PublishedAt is { } published ? new XElement(atom + "updated", published.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")) : null,
                            !string.IsNullOrWhiteSpace(item.AuthorName)
                                ? new XElement(atom + "author", new XElement(atom + "name", item.AuthorName)) : null,
                            new XElement(atom + "summary", summary),
                            fullText ? new XElement(atom + "content", new XAttribute("type", "html"), markdown.Render(item.Markdown, widths)) : null);
                    })));
            return Results.Text(ToXml(document), "application/atom+xml", Encoding.UTF8);
        });

        // oEmbed provider：僅 JSON；受保護與私密內容與不存在的網址一律 404。
        app.MapGet("/oembed", async (OrigamiClient api, HttpContext http, CancellationToken ct) =>
        {
            var format = http.Request.Query["format"].ToString();
            if (format.Length > 0 && !format.Equals("json", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { message = "僅支援 JSON 格式的 oEmbed 回應。" }, statusCode: StatusCodes.Status501NotImplemented);
            var target = ParseContentPath(http.Request.Query["url"].ToString());
            if (target is not { } contentPath) return Results.NotFound();
            PublicContent? detail;
            string path;
            if (contentPath.Kind == ContentKind.Page)
            {
                // 頁面以父鏈驗證整條路徑：中段不符即 404（與公開路由一致），網址用完整階層形式。
                var resolved = await api.ResolvePublishedPagePathAsync(contentPath.Segments, ct);
                if (resolved is null || !contentPath.Segments.SequenceEqual(resolved.Segments, StringComparer.Ordinal)) return Results.NotFound();
                detail = resolved.Content;
                path = "/pages/" + string.Join("/", resolved.Segments.Select(Uri.EscapeDataString));
            }
            else
            {
                detail = await api.GetPublishedDetailAsync(ContentKind.Post, contentPath.Segments[0], ct);
                path = "/posts/" + Uri.EscapeDataString(contentPath.Segments[0]);
            }
            if (detail is null || detail.IsProtected || detail.IsPrivate) return Results.NotFound();
            var site = await api.GetPublicSettingsAsync(ct) ?? PublicSiteSettings.Default;
            var url = BaseUrl(http) + path;
            var card = WebUtility.HtmlEncode(detail.Title) + " — " + WebUtility.HtmlEncode(site.Name);
            return Results.Json(new OembedPayload("1.0", "rich", detail.Title, site.Name, site.Name,
                url, "<a href=\"" + url + "\">" + card + "</a>"));
        });
    }

    private static string BaseUrl(HttpContext http) => http.Request.Scheme + "://" + http.Request.Host.Value;

    /// <summary>oEmbed 1.0 回應；欄位名依規範使用底線形式。</summary>
    private sealed record OembedPayload(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("author_name")] string AuthorName,
        [property: JsonPropertyName("provider_name")] string ProviderName,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("html")] string Html);

    private static string ToXml(XDocument document) => document.Declaration + Environment.NewLine + document;

    private static async Task<Dictionary<string, int>?> LoadWidthsIfFullTextAsync(
        OrigamiClient api, PublicSiteSettings site, CancellationToken ct) =>
        site.FeedShowFullText == true ? await api.GetPublicImageWidthsAsync(ct) ?? [] : null;

    private static IResult RenderRss(PublicSiteSettings site, List<ApiFeedItem> items, string titleSuffix,
        MarkdownService markdown, HttpContext http, Dictionary<string, int>? widths)
    {
        var fullText = site.FeedShowFullText == true;
        XNamespace contentNs = "http://purl.org/rss/1.0/modules/content/";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("rss", new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "content", contentNs), new XAttribute(XNamespace.Xmlns + "dc", dc),
                new XElement("channel",
                    new XElement("title", site.Name + titleSuffix),
                    new XElement("link", BaseUrl(http) + "/"),
                    new XElement("description", site.Description),
                    !string.IsNullOrWhiteSpace(site.AuthorDisplayName) ? new XElement("managingEditor", site.AuthorDisplayName) : null,
                    items.Select(item =>
                    {
                        var link = BaseUrl(http) + item.PublicPath;
                        var summary = item.ExcerptIsManual ? item.Excerpt : markdown.Excerpt(item.Excerpt);
                        return new XElement("item",
                            new XElement("title", item.Title),
                            new XElement("link", link),
                            new XElement("guid", new XAttribute("isPermaLink", "true"), link),
                            item.PublishedAt is { } published ? new XElement("pubDate", published.UtcDateTime.ToString("R")) : null,
                            !string.IsNullOrWhiteSpace(item.AuthorName) ? new XElement(dc + "creator", item.AuthorName) : null,
                            new XElement("description", summary),
                            fullText ? new XElement(contentNs + "encoded", markdown.Render(item.Markdown, widths)) : null);
                    }))));
        return Results.Text(ToXml(document), "application/rss+xml", Encoding.UTF8);
    }

    private static async Task<(PublicSiteSettings Settings, List<ApiFeedItem> Items, string TitleSuffix)?> TryLoadTypeFeedAsync(
        OrigamiClient api, string typeSlug, string? taxonomySlug, string? termSlug, CancellationToken ct)
    {
        var settings = await api.GetPublicSettingsAsync(ct) ?? PublicSiteSettings.Default;
        var types = await api.ListPublicTypesAsync(ct);
        var type = types.FirstOrDefault(x => string.Equals(x.Slug, typeSlug, StringComparison.OrdinalIgnoreCase));
        if (type is null) return null;
        var suffix = " · " + type.Name;
        if (!string.IsNullOrWhiteSpace(taxonomySlug) && !string.IsNullOrWhiteSpace(termSlug))
        {
            var termArchive = await api.GetPublicCustomTermAsync(type.Slug, taxonomySlug, termSlug, ct);
            if (termArchive is null) return null;
            suffix += " · " + termArchive.TermName;
        }
        var items = await api.GetFeedItemsAsync(typeSlug: type.Slug, taxonomy: taxonomySlug, term: termSlug, ct: ct) ?? [];
        return (settings, items, suffix);
    }

    /// <summary>feed 的共同載入：設定、過濾的 taxonomy／author／CPT／search 檢查與含本文的 feed 條目。</summary>
    private static async Task<(PublicSiteSettings Settings, List<ApiFeedItem> Items, string TitleSuffix)?> TryLoadFeedAsync(
        OrigamiClient api, HttpContext http, CancellationToken ct)
    {
        var settings = await api.GetPublicSettingsAsync(ct) ?? PublicSiteSettings.Default;
        var category = Filter(http, "category");
        var tag = Filter(http, "tag");
        var author = Filter(http, "author");
        var typeSlug = Filter(http, "type");
        var taxonomy = Filter(http, "taxonomy");
        var term = Filter(http, "term");
        var search = Filter(http, "q");

        TaxonomyView? categoryView = null, tagView = null;
        if (category is not null)
        {
            categoryView = await api.GetTaxonomyViewAsync(TaxonomyKind.Category, category, ct);
            if (categoryView is null) return null;
        }
        if (tag is not null)
        {
            tagView = await api.GetTaxonomyViewAsync(TaxonomyKind.Tag, tag, ct);
            if (tagView is null) return null;
        }
        var suffix = ChannelTitle("", categoryView, tagView);
        if (author is not null)
        {
            var authorProfile = await api.GetPublicAuthorAsync(author, ct);
            if (authorProfile is null) return null;
            suffix += " · " + authorProfile.DisplayName;
        }
        if (typeSlug is not null)
        {
            var types = await api.ListPublicTypesAsync(ct);
            var matchedType = types.FirstOrDefault(x => string.Equals(x.Slug, typeSlug, StringComparison.OrdinalIgnoreCase));
            if (matchedType is null) return null;
            suffix += " · " + matchedType.Name;
            if (taxonomy is not null && term is not null)
            {
                var termArchive = await api.GetPublicCustomTermAsync(matchedType.Slug, taxonomy, term, ct);
                if (termArchive is null) return null;
                suffix += " · " + termArchive.TermName;
            }
        }
        if (search is not null)
            suffix += " · 搜尋：" + search;

        var items = await api.GetFeedItemsAsync(categoryView?.Slug, tagView?.Slug, ct, author, typeSlug, taxonomy, term, search) ?? [];
        return (settings, items, suffix);
    }

    private static string? Filter(HttpContext http, string name)
    {
        var value = http.Request.Query[name].ToString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static string ChannelTitle(string siteName, TaxonomyView? category, TaxonomyView? tag) =>
        siteName + (category is null ? "" : " · " + category.Title) + (tag is null ? "" : " · #" + tag.Title);

    private static string FeedQuery(HttpContext http)
    {
        var parts = new List<string>();
        foreach (var key in new[] { "category", "tag", "author", "type", "taxonomy", "term", "q" })
        {
            var val = Filter(http, key);
            if (val is not null) parts.Add(key + "=" + Uri.EscapeDataString(val));
        }
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    /// <summary>
    /// 把 /posts/{slug}、/pages/{…}/{slug} 網址解析為內容目標（相對或絕對網址皆可）。
    /// Segments 為前綴之後的路徑段；文章恰一段，頁面至多六段（與公開路由一致）。
    /// </summary>
    private static (ContentKind Kind, string[] Segments)? ParseContentPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var path = url;
        var scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var rest = path[(scheme + 3)..];
            var slash = rest.IndexOf('/');
            if (slash < 0) return null;
            path = rest[slash..];
        }
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        return segments[0].ToLowerInvariant() switch
        {
            "posts" when segments.Length == 2 => (ContentKind.Post, segments[1..]),
            "pages" when segments.Length is >= 2 and <= 7 => (ContentKind.Page, segments[1..]),
            _ => null
        };
    }
}
