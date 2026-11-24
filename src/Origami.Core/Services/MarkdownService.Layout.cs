namespace Origami.Core.Services;

/// <summary>
/// 版面容器的渲染後處理。Markdig 的自訂容器（<c>::: name</c>）只產生 <c>&lt;div class="name"&gt;</c>；
/// 提示框以外的摺疊塊、分欄、卡片、頁籤、圖庫與圖片圖說都在「已消毒」的 HTML 上重組。
/// 新增的標籤與屬性一律由固定字串或消毒器輸出的已編碼文字組成，不帶入作者輸入的原始字串。
/// </summary>
public sealed partial class MarkdownService
{
    private const string ClosingDiv = "</div>";
    private const int LayoutTitleMax = 120;

    /// <summary>
    /// <c>::: details 標題</c>、<c>::: card 標題</c> 與 <c>::: tab 標題</c> 的標題改寫為容器內第一段粗體文字：
    /// Markdig 會丟掉冒號後面、名稱以外的文字，標題語法（#）又會進入章節目錄，因此不用。
    /// 過長的標題不丟棄，保留為一般段落。
    /// </summary>
    private string NormalizeLayoutFences(string markdown) =>
        LayoutFenceStart().Replace(markdown, match =>
        {
            var colons = match.Groups[1].Value;
            var name = match.Groups[2].Value.ToLowerInvariant();
            var title = match.Groups[3].Value.Replace("*", "").Trim();
            if (title.Length == 0) return colons + " " + name + "\n";
            return title.Length <= LayoutTitleMax
                ? colons + " " + name + "\n\n**" + title + "**\n\n"
                : colons + " " + name + "\n\n" + title + "\n\n";
        });

    // 以下各處取用的文字都來自消毒器輸出，已是 HTML 編碼狀態（&amp; 等），直接重用即可；
    // 再編碼一次會讓「A & B」顯示成「A &amp; B」。
    private string TransformDetails(string html) =>
        TransformDivs(html, "details", inner =>
        {
            var summary = "詳細內容";
            var firstP = FirstParagraph().Match(inner);
            // 第一段若是服務自身的目錄／嵌入佔位段落，不能拿來當標題，否則佔位被吃掉、嵌入跟著消失。
            if (firstP.Success && !firstP.Groups[1].Value.StartsWith("EMBED-ORIGAMI-", StringComparison.Ordinal))
            {
                var text = StripHtmlTags().Replace(firstP.Groups[1].Value, "").Trim();
                if (text.Length > 0 && text.Length <= 120)
                {
                    summary = text;
                    inner = inner[firstP.Length..].Trim();
                }
            }
            return "<details class=\"details-block\"><summary>" + summary + "</summary>" + inner + "</details>";
        });

    /// <summary>卡片：選用的第一段粗體文字成為卡片標題。</summary>
    private string TransformCards(string html) =>
        TransformDivs(html, "card", inner =>
            "<div class=\"card\">" + LayoutTitle().Replace(inner, "<p class=\"card-title\"><strong>$1</strong></p>", 1) + "</div>");

    /// <summary>
    /// 頁籤有兩種寫法：外層 <c>:::: tabs</c> 包住多個 <c>::: tab</c>，或多個 <c>::: tab</c> 相鄰並排自動成一組。
    /// Markdig 的一行結尾冒號會關閉所有同為三冒號的巢狀容器，因此外層必須比內層多一個冒號。
    /// 面板標題同時保留在內文中，未執行腳本時仍讀得到；腳本啟用後才隱藏標題並產生頁籤列。
    /// </summary>
    private string TransformTabs(string html)
    {
        html = TransformDivs(html, "tabs", inner => "<div class=\"tabs\">" + (TabPanels(inner) ?? inner) + "</div>");
        return GroupSiblingDivs(html, "tab", null, group => "<div class=\"tabs\">" + TabPanels(group) + "</div>");
    }

    /// <summary>把一段 HTML 內所有 tab 容器轉為面板；沒有任何 tab 時回傳 null。</summary>
    private string? TabPanels(string html)
    {
        var builder = new StringBuilder("<div class=\"tab-panels\">");
        var panels = 0;
        var index = 0;
        while (TryFindDiv(html, index, "tab", out var start, out var contentStart, out var end))
        {
            builder.Append(html, index, start - index);
            builder.Append(TabPanel(html[contentStart..(end - ClosingDiv.Length)]));
            panels++;
            index = end;
        }
        if (panels == 0) return null;
        builder.Append(html, index, html.Length - index);
        return builder.Append("</div>").ToString();
    }

    private string TabPanel(string content)
    {
        var title = "";
        var match = LayoutTitle().Match(content);
        if (match.Success)
        {
            title = StripHtmlTags().Replace(match.Groups[1].Value, "").Trim();
            content = "<p class=\"tab-title\"><strong>" + match.Groups[1].Value + "</strong></p>" + content[match.Length..];
        }
        return "<section class=\"tab-panel\" data-tab-title=\"" + title + "\">" + content + "</section>";
    }

    /// <summary>
    /// 分欄同樣有兩種寫法：外層 <c>:::: columns</c> 包住 <c>::: column</c>（Markdig 輸出已可直接排版），
    /// 或多個 <c>::: column</c> 相鄰並排自動成組；已在外層容器內的不重複包。
    /// </summary>
    private string TransformColumns(string html)
    {
        var wrapped = new List<(int Start, int End)>();
        var index = 0;
        while (TryFindDiv(html, index, "columns", out var start, out _, out var end))
        {
            wrapped.Add((start, end));
            index = end;
        }
        return GroupSiblingDivs(html, "column", wrapped, group => "<div class=\"columns\">" + group + "</div>");
    }

    /// <summary>
    /// 把「相鄰且中間只有空白」的多個同 class div 視為一組交給 <paramref name="wrap"/>；
    /// 單獨一個或落在 <paramref name="exclude"/> 範圍內的不處理。
    /// </summary>
    private string GroupSiblingDivs(string html, string cssClass, List<(int Start, int End)>? exclude, Func<string, string> wrap)
    {
        var builder = new StringBuilder(html.Length + 128);
        var index = 0;
        while (TryFindDiv(html, index, cssClass, out var start, out _, out var end))
        {
            var groupEnd = end;
            var count = 1;
            var skipped = exclude is not null && exclude.Any(range => start >= range.Start && start < range.End);
            if (skipped)
            {
                builder.Append(html, index, end - index);
                index = end;
                continue;
            }
            while (IsAdjacentDiv(html, groupEnd, cssClass, out var nextEnd))
            {
                groupEnd = nextEnd;
                count++;
            }
            builder.Append(html, index, start - index);
            builder.Append(count > 1 ? wrap(html[start..groupEnd]) : html[start..groupEnd]);
            index = groupEnd;
        }
        builder.Append(html, index, html.Length - index);
        return builder.ToString();
    }

    private static bool IsAdjacentDiv(string html, int from, string cssClass, out int end)
    {
        var next = from;
        while (next < html.Length && char.IsWhiteSpace(html[next])) next++;
        return TryFindDiv(html, next, cssClass, out var start, out _, out end) && start == next;
    }

    /// <summary>圖庫：容器內每張圖片成為一個帶圖說的 figure；段落標籤移除（p 不能包 figure），其餘內容保留。</summary>
    private string TransformGalleries(string html) =>
        TransformDivs(html, "gallery", inner =>
        {
            if (!GalleryImage().IsMatch(inner)) return "<div class=\"gallery\">" + inner + "</div>";
            var body = GalleryImage().Replace(inner, match =>
            {
                var tag = match.Value;
                var src = ImageSource().Match(tag);
                if (!src.Success) return "";
                var alt = ImageAlt().Match(tag);
                var title = ImageTitle().Match(tag);
                var figure = new StringBuilder("<figure class=\"gallery-item\"><img src=\"").Append(src.Groups[1].Value).Append('"');
                if (alt.Success) figure.Append(" alt=\"").Append(alt.Groups[1].Value).Append('"');
                figure.Append('>');
                if (title.Success && title.Groups[1].Value.Length > 0)
                    figure.Append("<figcaption>").Append(title.Groups[1].Value).Append("</figcaption>");
                return figure.Append("</figure>").ToString();
            });
            return "<div class=\"gallery\">" + ParagraphTag().Replace(body, "") + "</div>";
        });

    /// <summary>獨立成段的圖片帶 title（Markdown 圖片語法的引號文字）時，以圖說呈現在圖片下方。</summary>
    private string TransformCaptions(string html) =>
        CaptionedImage().Replace(html, match =>
            "<figure class=\"content-figure\">" + match.Groups[1].Value + "<figcaption>" + match.Groups[2].Value + "</figcaption></figure>");

    private string TransformLayout(string html)
    {
        html = TransformCards(html);
        html = TransformTabs(html);
        html = TransformColumns(html);
        html = TransformGalleries(html);
        return TransformCaptions(html);
    }

    /// <summary>
    /// 逐一處理 class 為 <paramref name="cssClass"/> 的 div。以成對的 div 開頭／結尾計數找結尾：
    /// 非貪婪 Regex 會在第一個巢狀結尾標籤就截斷。標籤未閉合時，該處之後的內容原樣保留。
    /// </summary>
    private static string TransformDivs(string html, string cssClass, Func<string, string> transform)
    {
        var builder = new StringBuilder(html.Length + 128);
        var index = 0;
        while (TryFindDiv(html, index, cssClass, out var start, out var contentStart, out var end))
        {
            builder.Append(html, index, start - index);
            builder.Append(transform(html[contentStart..(end - ClosingDiv.Length)]));
            index = end;
        }
        builder.Append(html, index, html.Length - index);
        return builder.ToString();
    }

    private static bool TryFindDiv(string html, int from, string cssClass, out int start, out int contentStart, out int end)
    {
        start = html.IndexOf("<div class=\"" + cssClass + "\">", from, StringComparison.Ordinal);
        contentStart = -1;
        end = -1;
        if (start < 0) return false;
        contentStart = start + "<div class=\"".Length + cssClass.Length + 2;
        end = FindDivEnd(html, contentStart);
        return end >= 0;
    }

    private static int FindDivEnd(string html, int contentStart)
    {
        var depth = 1;
        var index = contentStart;
        while (index < html.Length)
        {
            var open = html.IndexOf("<div", index, StringComparison.Ordinal);
            var close = html.IndexOf(ClosingDiv, index, StringComparison.Ordinal);
            if (close < 0) return -1;
            if (open >= 0 && open < close && IsDivStart(html, open + 4))
            {
                depth++;
                index = open + 4;
            }
            else
            {
                index = close + ClosingDiv.Length;
                if (--depth == 0) return index;
            }
        }
        return -1;
    }

    private static bool IsDivStart(string html, int afterName) =>
        afterName >= html.Length || html[afterName] == '>' || char.IsWhiteSpace(html[afterName]);

    [GeneratedRegex(@"(?im)^[\t ]*(:{3,})[\t ]*(details|card|tab)\b(?:[\t ]+([^\r\n]+))?[\t ]*\r?\n", RegexOptions.CultureInvariant)]
    private partial Regex LayoutFenceStart();

    [GeneratedRegex(@"^\s*<p><strong>(.*?)</strong></p>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private partial Regex LayoutTitle();

    [GeneratedRegex(@"<img[^>]*>", RegexOptions.CultureInvariant)]
    private partial Regex GalleryImage();

    [GeneratedRegex(@"\ssrc=""([^""]*)""", RegexOptions.CultureInvariant)]
    private partial Regex ImageSource();

    [GeneratedRegex(@"\salt=""([^""]*)""", RegexOptions.CultureInvariant)]
    private partial Regex ImageAlt();

    [GeneratedRegex(@"\stitle=""([^""]*)""", RegexOptions.CultureInvariant)]
    private partial Regex ImageTitle();

    [GeneratedRegex(@"</?p>", RegexOptions.CultureInvariant)]
    private partial Regex ParagraphTag();

    [GeneratedRegex(@"<p>(<img [^>]*?title=""([^""]+)""[^>]*>)</p>", RegexOptions.CultureInvariant)]
    private partial Regex CaptionedImage();
}
