namespace Origami.Core.Services;

/// <summary>
/// WordPress 內容是 HTML，本產品以 Markdown 為唯一正文格式。轉換只保留語意標記
/// （段落、標題、清單、連結、圖片、引用、程式碼），樣式與腳本一律丟棄；
/// 危險協定（javascript:、data:）的連結與圖片直接移除，不留可執行內容。
/// </summary>
public static class HtmlToMarkdown
{
    /// <summary>把 HTML 片段轉為 Markdown；無法解析時回傳去除標籤的純文字。</summary>
    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = html.Trim();
        // WordPress 匯出多為 XHTML；以容錯方式解析，遇到非 XML 內容則退回純文字。
        var root = ParseFragment(text) ?? new XElement("div", text);
        var builder = new System.Text.StringBuilder();
        Write(root, builder, 0);
        var result = builder.ToString();
        // 收斂多餘空行，保留段落間隔。
        while (result.Contains("\n\n\n", StringComparison.Ordinal))
            result = result.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        return result.Trim();
    }

    private static XElement? ParseFragment(string html)
    {
        try
        {
            var wrapped = "<div xmlns=\"http://www.w3.org/1999/xhtml\">" + html + "</div>";
            return XElement.Parse(wrapped, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static void Write(XElement node, System.Text.StringBuilder builder, int depth)
    {
        foreach (var child in node.Nodes())
        {
            switch (child)
            {
                case XText text:
                    builder.Append(EscapeText(text.Value));
                    break;
                case XElement element:
                    WriteElement(element, builder, depth);
                    break;
            }
        }
    }

    private static void WriteElement(XElement element, System.Text.StringBuilder builder, int depth)
    {
        var name = element.Name.LocalName.ToLowerInvariant();
        switch (name)
        {
            case "p" or "div" or "section" or "article" or "header" or "footer":
                EnsureBlankLine(builder);
                Write(element, builder, depth);
                EnsureBlankLine(builder);
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                EnsureBlankLine(builder);
                builder.Append('#', int.Parse(name[1..].ToString(System.Globalization.CultureInfo.InvariantCulture)));
                builder.Append(' ');
                Write(element, builder, depth);
                EnsureBlankLine(builder);
                break;
            case "br":
                builder.Append("  \n");
                break;
            case "strong" or "b":
                builder.Append("**");
                Write(element, builder, depth);
                builder.Append("**");
                break;
            case "em" or "i":
                builder.Append("*");
                Write(element, builder, depth);
                builder.Append("*");
                break;
            case "code" when element.Parent?.Name.LocalName.ToLowerInvariant() != "pre":
                builder.Append("`");
                Write(element, builder, depth);
                builder.Append("`");
                break;
            case "pre":
                EnsureBlankLine(builder);
                builder.Append("```\n");
                Write(element, builder, depth);
                builder.Append("\n```\n");
                EnsureBlankLine(builder);
                break;
            case "blockquote":
                EnsureBlankLine(builder);
                var inner = new System.Text.StringBuilder();
                Write(element, inner, depth);
                foreach (var line in inner.ToString().Trim('\n').Split('\n'))
                    builder.Append("> ").Append(line).Append('\n');
                EnsureBlankLine(builder);
                break;
            case "ul" or "ol":
                EnsureBlankLine(builder);
                var index = 1;
                foreach (var item in element.Elements().Where(x => x.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
                {
                    builder.Append(name == "ol" ? $"{index}. " : "- ");
                    Write(item, builder, depth + 1);
                    builder.Append('\n');
                    index++;
                }
                EnsureBlankLine(builder);
                break;
            case "li":
                Write(element, builder, depth);
                break;
            case "a":
                WriteLink(element, builder);
                break;
            case "img":
                WriteImage(element, builder);
                break;
            case "hr":
                EnsureBlankLine(builder);
                builder.Append("---");
                EnsureBlankLine(builder);
                break;
            case "script" or "style" or "noscript" or "iframe" or "object" or "embed" or "form" or "input":
                // 可執行或嵌入內容一律丟棄。
                break;
            case "table":
                EnsureBlankLine(builder);
                foreach (var row in element.Descendants().Where(x => x.Name.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase)))
                {
                    var cells = row.Elements().Where(x => x.Name.LocalName is "td" or "th")
                        .Select(cell => PlainText(cell).Replace("|", "\\|", StringComparison.Ordinal).Trim()).ToList();
                    if (cells.Count > 0)
                    {
                        builder.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                    }
                }
                EnsureBlankLine(builder);
                break;
            default:
                Write(element, builder, depth);
                break;
        }
    }

    private static void WriteLink(XElement element, System.Text.StringBuilder builder)
    {
        var href = element.Attribute("href")?.Value.Trim() ?? "";
        var text = PlainText(element);
        if (href.Length == 0 || !IsSafeUrl(href))
        {
            builder.Append(text);
            return;
        }
        builder.Append('[').Append(text).Append("](").Append(href).Append(')');
    }

    private static void WriteImage(XElement element, System.Text.StringBuilder builder)
    {
        var source = element.Attribute("src")?.Value.Trim() ?? "";
        if (source.Length == 0 || !IsSafeUrl(source)) return;
        var alt = element.Attribute("alt")?.Value.Trim() ?? "";
        builder.Append("![").Append(alt.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal))
            .Append("](").Append(source).Append(')');
    }

    /// <summary>只保留 http／https 與站內相對網址；javascript:、data: 等一律視為不安全。</summary>
    private static bool IsSafeUrl(string url)
    {
        if (url.Length == 0 || url.Any(char.IsControl)) return false;
        if (url.StartsWith("//", StringComparison.Ordinal)) return false;
        if (url.StartsWith('/')) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0;
    }

    private static string PlainText(XElement element)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var node in element.DescendantNodes())
        {
            if (node is XText text) builder.Append(text.Value);
        }
        return System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>逸出 Markdown 中會改變語意的字元，但保留既有換行與一般標點。</summary>
    private static string EscapeText(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);

    private static void EnsureBlankLine(System.Text.StringBuilder builder)
    {
        var length = builder.Length;
        if (length == 0) return;
        var tail = builder.ToString(length - 2, Math.Min(2, length));
        if (tail.Contains('\n') && !tail.Contains("\n\n", StringComparison.Ordinal)) builder.Append('\n');
        else if (!tail.Contains('\n', StringComparison.Ordinal)) builder.Append("\n\n");
    }
}
