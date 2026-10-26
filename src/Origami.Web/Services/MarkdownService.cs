namespace Origami.Web.Services;

public sealed partial class MarkdownService
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public string Render(string markdown, IReadOnlyDictionary<string, int>? mediaWidths = null)
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
        var html = sanitizer.Sanitize(Markdown.ToHtml(markdown, pipeline));
        // Raw HTML is disabled, so every <img> originates from Markdown syntax and lacks these
        // attributes; content images defer loading below the initial article viewport.
        html = html.Replace("<img ", "<img loading=\"lazy\" decoding=\"async\" ", StringComparison.Ordinal);
        return mediaWidths is { Count: > 0 } ? AddResponsiveSources(html, mediaWidths) : html;
    }

    /// <summary>Adds srcset candidates for /media images whose variants are known to exist on disk.</summary>
    private static string AddResponsiveSources(string html, IReadOnlyDictionary<string, int> mediaWidths)
    {
        return ContentImage().Replace(html, match =>
        {
            var fileName = match.Groups[2].Value;
            if (!mediaWidths.TryGetValue(fileName, out var originalWidth)) return match.Value;
            var candidates = Limits.ImageVariantWidths.Where(width => width < originalWidth).ToList();
            if (candidates.Count == 0) return match.Value;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var srcset = string.Join(", ", candidates.Select(width => "/media/" + stem + "_w" + width + extension + " " + width + "w"));
            return match.Groups[1].Value + fileName + match.Groups[3].Value + " srcset=\"" + srcset + "\"";
        });
    }

    [GeneratedRegex("""(<img [^>]*?src="/media/)([^"]+)(")""", RegexOptions.CultureInvariant)]
    private static partial Regex ContentImage();

    public string Excerpt(string markdown)
    {
        // List projections fetch only a bounded prefix; render the result as encoded text, never HTML.
        var text = Markdown.ToPlainText(markdown, pipeline);
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= 180) return text;
        var length = char.IsHighSurrogate(text[179]) && char.IsLowSurrogate(text[180]) ? 179 : 180;
        return text[..length].TrimEnd() + "…";
    }

    /// <summary>The first image link, or null when the content has no allowed image.</summary>
    public string? FirstImage(string markdown)
    {
        var match = FirstImageLink().Match(markdown);
        return match.Success && IsAllowedUrl(match.Groups[1].Value) ? match.Groups[1].Value : null;
    }

    /// <summary>Markdown reduced to plain, whitespace-collapsed text without truncating.</summary>
    public string PlainText(string markdown)
    {
        var text = Markdown.ToPlainText(markdown, pipeline);
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"^!\[[^\]]*\]\(\s*(<[^>]*>|[^)\s]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FirstImageLink();

    private static bool IsAllowedUrl(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) return false;
        if (value.StartsWith('#')) return true;
        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0;
    }
}
