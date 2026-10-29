namespace Origami.Core.Services;

public sealed partial class MarkdownService
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public string Render(string markdown, IReadOnlyDictionary<string, int>? mediaWidths = null)
    {
        if (markdown.Length > Limits.Body) throw new CmsException("本文超過長度上限。");
        // 分頁分隔符只是作者標記：全文渲染（feed 全文、嵌入等）一律剝除，不得變成可見文字。
        markdown = NextPageSeparator().Replace(markdown, "");
        // 白名單影片連結（獨立一行）先換成自製佔位段落；渲染後以同樣由本服務
        // 構造的 iframe 取代。佔位與 iframe 都不含作者輸入的原始字串，無法偽造任意網域。
        var embeds = new List<string>();
        markdown = VideoLinkLine().Replace(markdown, match =>
        {
            var html = VideoEmbed(match.Groups[1].Value);
            if (html is null) return match.Value;
            embeds.Add(html);
            return "\n\nEMBED-ORIGAMI-" + (embeds.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n\n";
        });
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
        for (var i = 0; i < embeds.Count; i++)
        {
            var marker = "<p>EMBED-ORIGAMI-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</p>";
            html = html.Replace(marker, embeds[i], StringComparison.Ordinal);
        }
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

    /// <summary>
    /// 長文分頁：以獨立一行的 <c>&lt;!--nextpage--&gt;</c>（與 WordPress 相容）切分本文。
    /// 沒有分隔時回傳單一元素；渲染、嵌入與匯出仍以完整原文為準，分頁只影響前台呈現。
    /// </summary>
    public static List<string> SplitPages(string markdown)
    {
        if (markdown.Length == 0) return [""];
        return [.. NextPageSeparator().Split(markdown)];
    }

    [GeneratedRegex(@"(?im)^[\t ]*<!--\s*nextpage\s*-->[\t ]*(?:\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex NextPageSeparator();

    /// <summary>獨立一行的白名單影片網址；行內提及或非白名單網域都不轉換。</summary>
    [GeneratedRegex(@"(?im)^[\t ]*(https?://(?:www\.youtube\.com/watch\?v=|m\.youtube\.com/watch\?v=|youtu\.be/|vimeo\.com/|player\.vimeo\.com/video/)[^\s]+)[\t ]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoLinkLine();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{5,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex YouTubeId();

    /// <summary>
    /// 把白名單影片網址轉為 lazy iframe。YouTube 使用 nocookie 網域與時間起點；
    /// 任何不完整的網址（缺 id、id 含非法字元）一律回 null，保留為純文字。
    /// </summary>
    private static string? VideoEmbed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Query.Length > 100 || uri.Fragment.Length > 0)
            return null;
        var query = ParseQuery(uri.Query);
        string html;
        if (uri.Host is "www.youtube.com" or "m.youtube.com" or "youtube.com")
        {
            if (uri.AbsolutePath != "/watch" || !query.TryGetValue("v", out var id) || !YouTubeId().IsMatch(id)) return null;
            var start = ParseStart(query.GetValueOrDefault("t"));
            html = $"<iframe class=\"video-embed\" src=\"https://www.youtube-nocookie.com/embed/{id}{(start > 0 ? "?start=" + start : "")}\" title=\"嵌入的 YouTube 影片\" loading=\"lazy\" allow=\"accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" allowfullscreen></iframe>";
        }
        else if (uri.Host == "youtu.be")
        {
            var id = uri.AbsolutePath.Trim('/');
            if (id.Length is < 5 or > 20 || !YouTubeId().IsMatch(id)) return null;
            var start = ParseStart(query.GetValueOrDefault("t"));
            html = $"<iframe class=\"video-embed\" src=\"https://www.youtube-nocookie.com/embed/{id}{(start > 0 ? "?start=" + start : "")}\" title=\"嵌入的 YouTube 影片\" loading=\"lazy\" allow=\"accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" allowfullscreen></iframe>";
        }
        else
        {
            // Vimeo 僅接受純數字影片編號；其他路徑形式一律不轉換。
            var id = uri.AbsolutePath.Trim('/');
            if (uri.Host == "player.vimeo.com") id = id.StartsWith("video/", StringComparison.Ordinal) ? id["video/".Length..] : "";
            if (id.Length is < 4 or > 12 || !id.All(char.IsAsciiDigit)) return null;
            html = $"<iframe class=\"video-embed\" src=\"https://player.vimeo.com/video/{id}\" title=\"嵌入的 Vimeo 影片\" loading=\"lazy\" allow=\"autoplay; fullscreen; picture-in-picture\" allowfullscreen></iframe>";
        }
        return html;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query.Length == 0) return result;
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0) continue;
            var key = part[..equals];
            var value = Uri.UnescapeDataString(part[(equals + 1)..]);
            if (!result.ContainsKey(key)) result[key] = value;
        }
        return result;
    }

    /// <summary>YouTube t 參數（90、1m30s）轉為秒；上限 24 小時，無法解析回 0。</summary>
    private static int ParseStart(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var total = 0;
        var current = 0;
        foreach (var ch in value)
        {
            if (char.IsAsciiDigit(ch)) { current = current * 10 + (ch - '0'); if (current > 100_000) return 0; }
            else if (ch is 'h' or 'm' or 's')
            {
                total += ch switch { 'h' => current * 3600, 'm' => current * 60, _ => current };
                current = 0;
            }
            else return 0;
        }
        total += current;
        return total is > 0 and <= 86_400 ? total : 0;
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
