namespace Origami.Web.Services;

public sealed class MarkdownService
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public string Render(string markdown)
    {
        if (markdown.Length > Limits.Body) throw new CmsException("本文超過長度上限。");
        // A fresh sanitizer avoids shared mutable state across concurrent SSR requests.
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "h1", "h2", "h3", "h4", "h5", "h6", "a", "img", "pre", "code",
            "blockquote", "ul", "ol", "li", "em", "strong", "br", "hr"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "src", "alt", "title"]);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https"]);
        sanitizer.FilterUrl += (_, e) =>
        {
            if (!IsAllowedUrl(e.SanitizedUrl)) e.SanitizedUrl = null;
        };
        return sanitizer.Sanitize(Markdown.ToHtml(markdown, pipeline));
    }

    private static bool IsAllowedUrl(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) return false;
        if (value.StartsWith('#')) return true;
        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0;
    }
}
