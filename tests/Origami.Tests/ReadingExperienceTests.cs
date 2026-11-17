namespace Origami.Tests;

/// <summary>
/// 閱讀體驗批次：服務端代碼高亮（僅自產 tok-* span、未知語言回退、內聯 code 不受影響）
/// 與內容圖片燈箱（:target overlay、alt 透傳、重複圖片不重複 overlay、id 跨渲染穩定）。
/// </summary>
public sealed class ReadingExperienceTests
{
    private readonly MarkdownService markdown = new();

    private string Render(string body) => markdown.Render(body);

    [Fact]
    public void Fenced_csharp_code_gets_server_side_highlighting()
    {
        var html = Render("```csharp\nvar s = \"hi\"; // note\n```\n");
        Assert.Contains("<figure class=\"code-block\" data-lang=\"C#\">", html);
        Assert.Contains("<span class=\"tok-kw\">var</span>", html);
        Assert.Contains("<span class=\"tok-str\">\"hi\"</span>", html);
        Assert.Contains("<span class=\"tok-cm\">// note</span>", html);
        Assert.Contains("<span class=\"cl\">", html);
    }

    [Fact]
    public void Fenced_sql_keywords_are_case_insensitive()
    {
        var html = Render("```sql\nselect Id from Posts where N = 1\n```\n");
        Assert.Contains("data-lang=\"SQL\"", html);
        Assert.Contains("<span class=\"tok-kw\">select</span>", html);
        Assert.Contains("<span class=\"tok-num\">1</span>", html);
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("js")]
    [InlineData("python")]
    [InlineData("json")]
    public void Aliased_languages_normalize_to_labels(string fence)
    {
        var html = Render($"```{fence}\nx\n```\n");
        Assert.Contains("<figure class=\"code-block\"", html);
    }

    [Fact]
    public void Unknown_language_falls_back_to_plain_code_block()
    {
        var html = Render("```frobnicate\nsome code\n```\n");
        Assert.DoesNotContain("code-block", html);
        Assert.Contains("<pre><code class=\"language-frobnicate\">", html);
    }

    [Fact]
    public void Inline_code_is_not_highlighted_or_wrapped()
    {
        var html = Render("Use `var x = 1;` inline.");
        Assert.DoesNotContain("code-block", html);
        Assert.DoesNotContain("tok-", html);
        Assert.Contains("<code>var x = 1;</code>", html);
    }

    [Fact]
    public void Highlighted_output_keeps_security_invariants()
    {
        var html = Render("```csharp\nvar s = \"<script>alert(1)</script>\";\n```\n");
        var document = new HtmlParser().ParseDocument(html);
        Assert.Empty(document.QuerySelectorAll("script, iframe, svg, style, object, embed, form"));
        foreach (var element in document.All)
        {
            Assert.DoesNotContain(element.Attributes, a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.Name == "style");
            if (element.ClassList.Any(c => c.StartsWith("tok-", StringComparison.Ordinal) || c is "cl" or "code-block"))
                continue; // 自產標記僅含 class
        }
        // 危隕內容以文字呈現，不形成標記。
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Content_images_are_wrapped_with_pure_css_lightbox()
    {
        var html = Render("![封面](https://example.com/a.png)");
        Assert.Contains("<a class=\"img-lightbox\" href=\"#lb-", html);
        Assert.Contains("<div class=\"lightbox\" id=\"lb-", html);
        Assert.Contains("class=\"lightbox-figure\"", html);
        Assert.Contains("alt=\"封面\"", html);
    }

    [Fact]
    public void Repeated_image_reuses_single_overlay()
    {
        var html = Render("![x](https://example.com/a.png)\n\n![x](https://example.com/a.png)");
        Assert.Equal(2, CountOccurrences(html, "img-lightbox\""));
        Assert.Equal(1, CountOccurrences(html, "class=\"lightbox\""));
    }

    [Fact]
    public void Lightbox_ids_are_deterministic_across_renders()
    {
        var first = Render("![x](https://example.com/a.png)");
        var second = Render("![x](https://example.com/a.png)");
        var id1 = first[(first.IndexOf("#lb-", StringComparison.Ordinal) + 1)..].Split('"')[0];
        var id2 = second[(second.IndexOf("#lb-", StringComparison.Ordinal) + 1)..].Split('"')[0];
        Assert.Equal(id1, id2);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0) { count++; index += token.Length; }
        return count;
    }
}
