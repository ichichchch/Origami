namespace Origami.Core.Services;

public sealed partial class MarkdownService
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.AutoLink)
        .UseFootnotes()
        .UseCustomContainers()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseTaskLists()
        .Build();

    public string Render(string markdown, IReadOnlyDictionary<string, int>? mediaWidths = null)
    {
        if (markdown.Length > Limits.Body) throw new CmsException("本文超過長度上限。");
        // 分頁分隔符只是作者標記：全文渲染（feed 全文、嵌入等）一律剝除，不得變成可見文字。
        markdown = NextPageSeparator().Replace(markdown, "");
        var hasToc = TocMarker().IsMatch(markdown);
        if (hasToc)
            markdown = TocMarker().Replace(markdown, "\n\nTOC-ORIGAMI-MARKER\n\n");
        markdown = DetailsFenceStart().Replace(markdown, match =>
        {
            var title = match.Groups[1].Value.Trim();
            return title.Length > 0 ? $"::: details\n{title}\n\n" : "::: details\n";
        });
        // 白名單影片連結（獨立一行）與站內音訊／影片／文件連結先換成自製佔位段落；渲染後以同樣由本服務
        // 構造的標籤取代。佔位與輸出都不含作者輸入的原始字串，無法偽造任意網域或屬性。
        var embeds = new List<string>();
        markdown = VideoLinkLine().Replace(markdown, match =>
        {
            var html = VideoEmbed(match.Groups[1].Value);
            if (html is null) return match.Value;
            embeds.Add(html);
            return "\n\nEMBED-ORIGAMI-" + (embeds.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n\n";
        });
        markdown = MediaLinkLine().Replace(markdown, match =>
        {
            var label = match.Groups[1].Value;
            var fileName = match.Groups[2].Value;
            var html = LocalMediaEmbed(label, fileName);
            if (html is null) return match.Value;
            embeds.Add(html);
            return "\n\nEMBED-ORIGAMI-" + (embeds.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n\n";
        });
        // 每次建立新的清理器，避免並發 SSR 請求之間共用可變狀態。
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "h1", "h2", "h3", "h4", "h5", "h6", "a", "img", "pre", "code",
            "blockquote", "ul", "ol", "li", "em", "strong", "del", "sup", "sub", "mark", "br", "hr",
            "div", "section", "aside", "nav", "table", "thead", "tbody", "tr", "th", "td", "input"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "src", "alt", "title", "id", "class", "role", "aria-label", "type", "checked", "disabled"]);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https"]);
        sanitizer.FilterUrl += (_, e) =>
        {
            if (!IsAllowedUrl(e.SanitizedUrl)) e.SanitizedUrl = null;
        };
        var html = sanitizer.Sanitize(Markdown.ToHtml(markdown, pipeline));
        // ::: details 標題 -> 轉換為原生可摺疊 <details><summary> 面板
        html = DetailsContainer().Replace(html, match =>
        {
            var inner = match.Groups[1].Value.Trim();
            var summary = "詳細內容";
            var firstP = FirstParagraph().Match(inner);
            if (firstP.Success)
            {
                var text = StripHtmlTags().Replace(firstP.Groups[1].Value, "").Trim();
                if (text.Length > 0 && text.Length <= 120)
                {
                    summary = WebUtility.HtmlEncode(text);
                    inner = inner[firstP.Length..].Trim();
                }
            }
            return $"<details class=\"details-block\"><summary>{summary}</summary>{inner}</details>";
        });
        if (hasToc)
        {
            var tocHtml = BuildTocHtml(html);
            html = html.Replace("<p>TOC-ORIGAMI-MARKER</p>", tocHtml, StringComparison.Ordinal);
        }
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

    /// <summary>計算純文字字數（CJK 字元逐字計數，西文依單字計數）與預計閱讀分鐘數（每分鐘約 300 字，最少 1 分鐘）。</summary>
    public (int WordCount, int ReadingMinutes) WordCountAndReadingTime(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return (0, 1);
        var clean = NextPageSeparator().Replace(markdown, " ");
        clean = TocMarker().Replace(clean, " ");
        var plain = PlainText(clean);
        if (plain.Length == 0) return (0, 1);
        var count = 0;
        var inAsciiWord = false;
        foreach (var ch in plain)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                inAsciiWord = false;
                continue;
            }
            if (ch > 0x7F)
            {
                count++;
                inAsciiWord = false;
            }
            else if (!inAsciiWord)
            {
                count++;
                inAsciiWord = true;
            }
        }
        var minutes = Math.Max(1, (int)Math.Ceiling(count / 300.0));
        return (count, minutes);
    }

    private static string? LocalMediaEmbed(string label, string fileName)
    {
        if (!ContentRules.IsSafeMediaFileNameAny(fileName)) return null;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var safeUrl = "/media/" + fileName;
        var safeLabel = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(label) ? fileName : label.Trim());
        return ext switch
        {
            ".mp3" or ".m4a" or ".ogg" or ".wav" =>
                $"<figure class=\"media-embed audio-embed\"><audio controls preload=\"metadata\" src=\"{safeUrl}\"></audio><figcaption><a href=\"{safeUrl}\" download>{safeLabel}</a></figcaption></figure>",
            ".mp4" or ".webm" =>
                $"<figure class=\"media-embed video-file-embed\"><video controls preload=\"metadata\" src=\"{safeUrl}\"></video><figcaption><a href=\"{safeUrl}\" download>{safeLabel}</a></figcaption></figure>",
            ".pdf" =>
                $"<div class=\"media-embed file-card\"><a class=\"file-download-link\" href=\"{safeUrl}\" target=\"_blank\" rel=\"noopener\">📄 {safeLabel}</a></div>",
            _ => null
        };
    }

    [GeneratedRegex(@"(?im)^[\t ]*\[([^\]]*)\]\(/media/([a-f0-9]{32}\.(?:mp3|m4a|ogg|wav|mp4|webm|pdf))\)[\t ]*(?:\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex MediaLinkLine();

    [GeneratedRegex(@"<div class=""details"">(.*?)</div>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DetailsContainer();

    [GeneratedRegex(@"^\s*<p>(.*?)</p>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FirstParagraph();

    private static string BuildTocHtml(string html)
    {
        var matches = HeadingTag().Matches(html);
        if (matches.Count == 0) return "";
        var sb = new StringBuilder("<nav class=\"toc\" aria-label=\"目錄\"><ul>");
        foreach (Match match in matches)
        {
            var level = match.Groups[1].Value;
            var id = match.Groups[2].Value;
            var inner = StripHtmlTags().Replace(match.Groups[3].Value, "").Trim();
            if (inner.Length == 0 || id.Length == 0) continue;
            sb.Append("<li class=\"toc-h").Append(level).Append("\"><a href=\"#")
              .Append(WebUtility.HtmlEncode(id)).Append("\">")
              .Append(WebUtility.HtmlEncode(inner)).Append("</a></li>");
        }
        sb.Append("</ul></nav>");
        return sb.ToString();
    }

    [GeneratedRegex(@"(?im)^[\t ]*\[toc\][\t ]*(?:\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex TocMarker();

    [GeneratedRegex(@"(?im)^[\t ]*:::[\t ]*details(?:[\t ]+([^\r\n]+))?[\t ]*\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex DetailsFenceStart();

    [GeneratedRegex(@"<h([2-4])\s+[^>]*?id=""([^""]+)""[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingTag();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex StripHtmlTags();

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
