namespace Origami.Tests;

/// <summary>單篇長文分頁：分隔符切分、非獨立行不切、全文渲染剝除分隔符。</summary>
public sealed class ContentPaginationTests
{
    private readonly MarkdownService markdown = new();

    [Fact]
    public void SplitPages_splits_only_on_standalone_nextpage_lines()
    {
        var sections = MarkdownService.SplitPages("第一頁\n\n<!--nextpage-->\n\n第二頁\n  <!--nextpage-->\n第三頁");
        Assert.Equal(3, sections.Count);
        Assert.Contains("第一頁", sections[0]);
        Assert.Contains("第二頁", sections[1]);
        Assert.Contains("第三頁", sections[2]);

        // 行內提及分隔符不是分頁指令；前後有文字的行不可觸發切分。
        Assert.Single(MarkdownService.SplitPages("參考 <!--nextpage--> 語法的說明文字"));
        // 大小寫與空白差異可容忍，但必須獨立成行。
        Assert.Equal(2, MarkdownService.SplitPages("a\n<!-- NEXTPAGE -->\nb").Count);
        // 無分隔即單一頁；空本文同樣回傳單一頁。
        Assert.Single(MarkdownService.SplitPages("沒有分頁的內容"));
        Assert.Single(MarkdownService.SplitPages(""));
    }

    [Fact]
    public void Full_text_rendering_strips_page_markers_instead_of_printing_them()
    {
        var html = markdown.Render("段落一\n\n<!--nextpage-->\n\n段落二");
        Assert.Contains("段落一", html);
        Assert.Contains("段落二", html);
        Assert.DoesNotContain("nextpage", html, StringComparison.OrdinalIgnoreCase);
    }
}
