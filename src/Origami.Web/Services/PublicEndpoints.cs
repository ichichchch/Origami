namespace Origami.Web.Services;

/// <summary>
/// 機器端公開端點：robots.txt、sitemap.xml 與 RSS 2.0 feed。每次請求都讀取即時的
/// 已發布狀態，因此撤回或刪除的內容會立即消失。
/// </summary>
public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (HttpContext http) => Results.Text(
            "User-agent: *\nDisallow: /admin\nDisallow: /account\nDisallow: /preview\n\nSitemap: " + BaseUrl(http) + "/sitemap.xml\n",
            "text/plain", Encoding.UTF8));

        app.MapGet("/sitemap.xml", async (ContentService content, HttpContext http, CancellationToken ct) =>
        {
            var items = await content.SitemapAsync(ct);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement(ns + "urlset", items.Select(item => new XElement(ns + "url",
                    new XElement(ns + "loc", BaseUrl(http) + item.Path),
                    item.LastModified is { } last ? new XElement(ns + "lastmod", last.UtcDateTime.ToString("yyyy-MM-dd")) : null))));
            return Results.Text(ToXml(document), "application/xml", Encoding.UTF8);
        });

        app.MapGet("/feed", async (ContentService content, SettingsService settings, MarkdownService markdown,
            HttpContext http, CancellationToken ct) =>
        {
            var site = await settings.GetAsync(ct);
            var posts = await content.ListPublishedAsync(1, site.FeedItems ?? Limits.FeedItems, ct);
            var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("rss", new XAttribute("version", "2.0"),
                    new XElement("channel",
                        new XElement("title", site.Name),
                        new XElement("link", BaseUrl(http) + "/"),
                        new XElement("description", site.Description),
                        posts.Items.Select(post =>
                        {
                            var link = BaseUrl(http) + "/posts/" + Uri.EscapeDataString(post.Slug);
                            return new XElement("item",
                                new XElement("title", post.Title),
                                new XElement("link", link),
                                new XElement("guid", new XAttribute("isPermaLink", "true"), link),
                                post.PublishedAt is { } published ? new XElement("pubDate", published.UtcDateTime.ToString("R")) : null,
                                // 手動摘要是作者輸入的純文字；備援取得的前綴仍含 Markdown。
                                new XElement("description", post.ExcerptIsManual ? post.Excerpt : markdown.Excerpt(post.Excerpt)));
                        }))));
            return Results.Text(ToXml(document), "application/rss+xml", Encoding.UTF8);
        });
    }

    private static string BaseUrl(HttpContext http) => http.Request.Scheme + "://" + http.Request.Host.Value;

    private static string ToXml(XDocument document) => document.Declaration + Environment.NewLine + document;
}
