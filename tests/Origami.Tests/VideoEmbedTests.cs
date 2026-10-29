namespace Origami.Tests;

/// <summary>白名單影片嵌入：獨立一行的 YouTube/Vimeo 網址轉 lazy iframe，其餘一律保持純文字。</summary>
public sealed class VideoEmbedTests
{
    private readonly MarkdownService markdown = new();

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/123456789", "https://player.vimeo.com/video/123456789")]
    [InlineData("https://player.vimeo.com/video/123456789", "https://player.vimeo.com/video/123456789")]
    public void Standalone_whitelisted_links_become_lazy_iframes(string link, string embedSource)
    {
        var html = markdown.Render("前文\n\n" + link + "\n\n後文");
        Assert.Contains("<iframe class=\"video-embed\" src=\"" + embedSource, html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.DoesNotContain("<p>" + link + "</p>", html);
    }

    [Fact]
    public void YouTube_start_time_is_carried_into_the_embed_url()
    {
        var html = markdown.Render("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1m30s");
        Assert.Contains("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?start=90", html);
    }

    [Theory]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]      // 非白名單網域
    [InlineData("https://www.youtube.com/watch")]                  // 缺影片編號
    [InlineData("https://vimeo.com/not-numeric")]                  // Vimeo 編號非數字
    [InlineData("https://youtu.be/ab")]                            // 編號過短
    public void Unrecognized_links_stay_as_plain_text(string link)
    {
        var html = markdown.Render(link);
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains(link, html);
    }

    [Fact]
    public void Inline_mentions_and_neighboring_text_do_not_embed()
    {
        var html = markdown.Render("這裡提到 https://youtu.be/dQw4w9WgXcQ 只是行內文字");
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("youtu.be", html);
    }

    [Fact]
    public void Malformed_marker_text_cannot_forge_an_iframe()
    {
        // 作者自行輸入佔位文字不會被當成嵌入指令；只有服務產生的配對才會替換。
        var html = markdown.Render("EMBED-ORIGAMI-0");
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("EMBED-ORIGAMI-0", html);
    }
}
