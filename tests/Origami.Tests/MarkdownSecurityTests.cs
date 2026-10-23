namespace Origami.Tests;

public sealed class MarkdownSecurityTests
{
    private readonly MarkdownService markdown = new();

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("![image](javascript:alert(1))")]
    [InlineData("[click](JaVaScRiPt:alert(1))")]
    [InlineData("[click](jav&#x61;script:alert(1))")]
    [InlineData("[click](java&#x09;script:alert(1))")]
    [InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("![image](data:image/svg+xml;base64,PHN2Zz4=)")]
    [InlineData("[click](vbscript:msgbox(1))")]
    [InlineData("[click](file:///C:/Windows/win.ini)")]
    [InlineData("[click](//untrusted.example/path)")]
    [InlineData("[click](mailto:test@example.com)")]
    [InlineData("<https://example.com> <script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)><iframe srcdoc='<script>alert(1)</script>'></iframe>")]
    [InlineData("<svg onload=alert(1)></svg><a href='javascript:alert(1)'>link</a>")]
    public void Markdown_never_emits_executable_markup_or_unsafe_urls(string input)
    {
        var document = new HtmlParser().ParseDocument(markdown.Render(input));
        Assert.Empty(document.QuerySelectorAll("script, iframe, svg, style, object, embed, form"));
        foreach (var element in document.All)
        {
            Assert.DoesNotContain(element.Attributes, a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || a.Name == "style");
            foreach (var name in new[] { "href", "src" })
            {
                if (element.GetAttribute(name) is not { } url) continue;
                Assert.True(url.StartsWith('#') || (url.StartsWith('/') && !url.StartsWith("//"))
                    || Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", url);
            }
        }
    }

    [Fact]
    public void Safe_markdown_keeps_formatting_links_and_uploaded_images()
    {
        var document = new HtmlParser().ParseDocument(markdown.Render("# 標題\n\n**重點**\n\n[網站](https://example.com)\n\n![圖片](/media/abc.png)\n\n`<script>`"));
        Assert.Equal("標題", document.QuerySelector("h1")!.TextContent);
        Assert.Equal("重點", document.QuerySelector("strong")!.TextContent);
        Assert.Equal("https://example.com", document.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("/media/abc.png", document.QuerySelector("img")!.GetAttribute("src"));
        Assert.Equal("<script>", document.QuerySelector("code")!.TextContent);
    }

    [Fact]
    public void Body_limit_is_enforced() => Assert.Throws<CmsException>(() => markdown.Render(new string('x', Limits.Body + 1)));

    [Fact]
    public void The_editor_preview_uses_the_same_sanitizing_renderer_as_the_public_page()
    {
        // The preview shows unpublished text, so it must apply exactly the same rules.
        var preview = markdown.Render("<script>alert(1)</script> [link](javascript:alert(2)) <img src=x onerror=alert(3)>");
        var document = new HtmlParser().ParseDocument(preview);
        Assert.Empty(document.QuerySelectorAll("script, img[onerror]"));
        Assert.Empty(document.QuerySelectorAll("a[href^='javascript:']"));
    }

    [Fact]
    public async Task Rendering_is_safe_for_concurrent_requests()
    {
        var expected = markdown.Render("**內容** [連結](https://example.com)");
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => markdown.Render("**內容** [連結](https://example.com)"))));
        Assert.All(results, result => Assert.Equal(expected, result));
    }
}
