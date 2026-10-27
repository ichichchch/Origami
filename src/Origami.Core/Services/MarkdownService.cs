namespace Origami.Core.Services;

public sealed partial class MarkdownService
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public string Render(string markdown, IReadOnlyDictionary<string, int>? mediaWidths = null)
    {
        if (markdown.Length > Limits.Body) throw new CmsException("本文超過長度上限。");
        // 每次建立新的清理器，避免並發 SSR 請求之間共用可變狀態。
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
        // 原始 HTML 已停用，因此每個 <img> 都來自 Markdown 語法且缺少這些
        // 屬性；內文圖片在文章首屏以下延後載入。
        html = html.Replace("<img ", "<img loading=\"lazy\" decoding=\"async\" ", StringComparison.Ordinal);
        return mediaWidths is { Count: > 0 } ? AddResponsiveSources(html, mediaWidths) : html;
    }

    /// <summary>為磁碟上已知存在變體的 /media 圖片加上 srcset 候選。</summary>
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
        // 列表投影只取有限前綴；結果以編碼文字輸出，絕不是 HTML。
        var text = Markdown.ToPlainText(markdown, pipeline);
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= 180) return text;
        var length = char.IsHighSurrogate(text[179]) && char.IsLowSurrogate(text[180]) ? 179 : 180;
        return text[..length].TrimEnd() + "…";
    }

    /// <summary>第一個圖片連結；內容沒有允許的圖片時為 null。</summary>
    public string? FirstImage(string markdown)
    {
        var match = FirstImageLink().Match(markdown);
        return match.Success && IsAllowedUrl(match.Groups[1].Value) ? match.Groups[1].Value : null;
    }

    /// <summary>把 Markdown 轉為純文字並摺疊空白，不截斷。</summary>
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
