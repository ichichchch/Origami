namespace Origami.Web.Services;

/// <summary>
/// Machine-facing public endpoints: robots.txt, sitemap.xml and the RSS 2.0 feed. They read the
/// live published state on every request, so withdrawn or deleted content disappears immediately.
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
            var posts = await content.ListPublishedAsync(1, Limits.FeedItems, ct);
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
                                // A manual excerpt is plain authored text; the fallback prefix still contains Markdown.
                                new XElement("description", post.ExcerptIsManual ? post.Excerpt : markdown.Excerpt(post.Excerpt)));
                        }))));
            return Results.Text(ToXml(document), "application/rss+xml", Encoding.UTF8);
        });
    }

    private static string BaseUrl(HttpContext http) => http.Request.Scheme + "://" + http.Request.Host.Value;

    private static string ToXml(XDocument document) => document.Declaration + Environment.NewLine + document;
}
