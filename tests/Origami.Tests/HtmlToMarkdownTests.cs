namespace Origami.Tests;

/// <summary>HTML 轉 Markdown：剪貼簿與網頁來源多半不是合法 XML，需以容錯解析保留語意標記並丟棄可執行內容。</summary>
public sealed class HtmlToMarkdownTests
{
    [Fact]
    public void Non_well_formed_html_is_parsed_and_keeps_semantic_markup()
    {
        var markdown = HtmlToMarkdown.Convert(
            "<meta charset='utf-8'><p>第一段&nbsp;A &amp; B<br>未閉合的 <b>粗體</b> 與 <i>斜體</i></p>" +
            "<h2>標題</h2><ul><li>項目一<li>項目二</ul>" +
            "<a href=\"https://example.com/page\">連結</a> " +
            "<img src=\"https://example.com/a.png\" alt=\"圖片\">");
        Assert.Contains("第一段\u00A0A & B  \n未閉合的 **粗體** 與 *斜體*", markdown);
        Assert.Contains("## 標題", markdown);
        Assert.Contains("- 項目一\n- 項目二", markdown);
        Assert.Contains("[連結](https://example.com/page)", markdown);
        Assert.Contains("![圖片](https://example.com/a.png)", markdown);
        Assert.DoesNotContain("<", markdown.Replace("&lt;", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Executable_and_unsafe_content_is_dropped_from_pasted_html()
    {
        var markdown = HtmlToMarkdown.Convert(
            "<p onclick=\"alert(1)\">文字<script>alert(2)</script>" +
            "<a href=\"javascript:alert(3)\">危險連結</a>" +
            "<img src=\"data:image/svg+xml,x\" alt=\"x\">" +
            "<iframe src=\"https://evil.example\"></iframe></p>");
        Assert.Equal("文字危險連結", markdown);
    }

    [Fact]
    public void Tables_keep_pipes_escaped_and_plain_fragments_fall_back_to_text()
    {
        var markdown = HtmlToMarkdown.Convert("<table><tr><th>名稱</th><th>a|b</th></tr><tr><td>值</td><td>1</td></tr></table>");
        Assert.Contains("| 名稱 | a\\|b |", markdown);
        Assert.Contains("| 值 | 1 |", markdown);
        Assert.Equal("純文字片段", HtmlToMarkdown.Convert("純文字片段"));
        Assert.Equal("", HtmlToMarkdown.Convert("   "));
    }

    [Fact]
    public void Well_formed_xhtml_still_takes_the_xml_path()
    {
        var markdown = HtmlToMarkdown.Convert("<p>段落 <strong>粗體</strong></p><pre><code>  x = 1;</code></pre>");
        Assert.Contains("段落 **粗體**", markdown);
        Assert.Contains("```\n  x = 1;\n```", markdown);
    }
}
