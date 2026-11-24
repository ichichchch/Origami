namespace Origami.Tests;

/// <summary>版面容器（摺疊塊、卡片、頁籤、分欄、圖庫、圖說）與 Bilibili 嵌入的渲染結果。</summary>
public sealed class MarkdownLayoutTests
{
    private readonly MarkdownService markdown = new();

    private static string Media(char fill) => "/media/" + new string(fill, 32) + ".png";

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    [Fact]
    public void Adjacent_columns_form_one_group_and_a_lone_column_is_not_wrapped()
    {
        var html = markdown.Render("::: column\n左欄\n:::\n::: column\n右欄\n:::\n\n其他段落\n\n::: column\n獨欄\n:::");
        Assert.Contains("<div class=\"columns\"><div class=\"column\"><p>左欄</p>", html);
        Assert.Contains("<div class=\"column\"><p>右欄</p>", html);
        Assert.Equal(1, CountOccurrences(html, "class=\"columns\""));
        Assert.Contains("<p>其他段落</p>", html);
        Assert.Contains("<div class=\"column\"><p>獨欄</p>", html);
    }

    [Fact]
    public void Columns_wrapped_in_an_outer_container_are_not_wrapped_twice()
    {
        var html = markdown.Render(":::: columns\n::: column\n左欄\n:::\n::: column\n右欄\n:::\n::::");
        Assert.Contains("<div class=\"columns\">" + "<div class=\"column\"><p>左欄</p>", html.Replace("\n", "", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(html, "class=\"columns\""));
        Assert.Contains("<p>右欄</p>", html);
    }

    [Fact]
    public void Card_title_becomes_a_title_paragraph()
    {
        var html = markdown.Render("::: card 重點公告\n內容一段。\n:::\n\n::: card\n純內容。\n:::");
        Assert.Contains("<div class=\"card\"><p class=\"card-title\"><strong>重點公告</strong></p>", html);
        Assert.Contains("<p>內容一段。</p>", html);
        Assert.Contains("<div class=\"card\"><p>純內容。</p>", html);
    }

    [Fact]
    public void Overlong_card_title_is_kept_as_an_ordinary_paragraph()
    {
        var title = new string('標', 121);
        var html = markdown.Render("::: card " + title + "\n內容。\n:::");
        Assert.DoesNotContain("card-title", html);
        Assert.Contains("<p>" + title + "</p>", html);
        Assert.Contains("<p>內容。</p>", html);
    }

    [Fact]
    public void Card_with_four_colons_keeps_its_title_and_holds_a_nested_container()
    {
        var html = markdown.Render(":::: card 標題\n::: note\n提示\n:::\n內容\n::::");
        Assert.Contains("<div class=\"card\"><p class=\"card-title\"><strong>標題</strong></p>", html);
        Assert.Contains("<div class=\"note\"><p>提示</p>", html);
        Assert.Contains("<p>內容</p>", html);
        Assert.EndsWith("</div>", html.TrimEnd());
    }

    [Fact]
    public void Titles_with_special_characters_are_not_encoded_twice()
    {
        var html = markdown.Render("[toc]\n\n:::: details A & B\n::: note\n提示\n:::\n\n結尾段落\n::::\n\n::: tab <標題> & 內容\n甲\n:::\n::: tab 二\n乙\n:::\n\n## 章節 & 小節\n\n正文");
        Assert.Contains("<summary>A &amp; B</summary>", html);
        Assert.DoesNotContain("&amp;amp;", html);
        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("<標題> & 內容", document.QuerySelector(".tab-panel")!.GetAttribute("data-tab-title"));
        Assert.Equal("章節 & 小節", document.QuerySelector(".toc a")!.TextContent);
    }

    [Fact]
    public void Adjacent_tabs_form_one_group_and_titles_stay_readable_without_script()
    {
        var html = markdown.Render("::: tab 第一頁\n內容甲\n:::\n::: tab 第二頁\n內容乙\n:::\n\n其他段落");
        Assert.Contains("<div class=\"tabs\"><div class=\"tab-panels\"><section class=\"tab-panel\" data-tab-title=\"第一頁\"><p class=\"tab-title\"><strong>第一頁</strong></p>", html);
        Assert.Contains("<p>內容甲</p>", html);
        Assert.Contains("<section class=\"tab-panel\" data-tab-title=\"第二頁\"><p class=\"tab-title\"><strong>第二頁</strong></p>", html);
        Assert.Contains("<p>內容乙</p>", html);
        Assert.Contains("</section></div></div>", html);
        Assert.Contains("<p>其他段落</p>", html);
        Assert.DoesNotContain("<div class=\"tab\">", html);
    }

    [Fact]
    public void Tabs_wrapped_in_an_outer_container_keep_content_before_the_first_tab()
    {
        // Markdig 以相同冒號數的結尾行一次關閉所有同層級容器，因此內層必須比外層少一個冒號。
        var html = markdown.Render(":::: tabs\n前言\n\n::: tab\n甲\n:::\n::: tab 乙\n乙\n:::\n::::");
        Assert.Contains("<div class=\"tabs\"><div class=\"tab-panels\"><p>前言</p>", html);
        Assert.Contains("<section class=\"tab-panel\" data-tab-title=\"\"><p>甲</p>", html);
        Assert.Contains("data-tab-title=\"乙\"><p class=\"tab-title\"><strong>乙</strong></p>", html);
        Assert.Contains("<p>乙</p>", html);
        Assert.DoesNotContain("<div class=\"tab\">", html);
        Assert.DoesNotContain("<div></div>", html);
    }

    [Fact]
    public void A_lone_tab_is_left_as_a_plain_div()
    {
        var html = markdown.Render("::: card\n::: tab\n乙\n:::\n:::");
        Assert.Contains("<div class=\"tab\"><p>乙</p>", html);
        Assert.DoesNotContain("tab-panel", html);
    }

    [Fact]
    public void Gallery_images_become_figures_with_captions()
    {
        var html = markdown.Render("::: gallery\n![甲](" + Media('a') + " \"圖說甲\")\n![乙](" + Media('b') + ")\n:::");
        // 圖庫圖片保留圖說，並與其他內文圖片一樣帶純 CSS 燈箱錨點。
        Assert.Contains("<figure class=\"gallery-item\"><a class=\"img-lightbox\" href=\"#lb-", html);
        Assert.Contains("<img loading=\"lazy\" decoding=\"async\" src=\"" + Media('a') + "\" alt=\"甲\"></a><figcaption>圖說甲</figcaption></figure>", html);
        Assert.Contains("<img loading=\"lazy\" decoding=\"async\" src=\"" + Media('b') + "\" alt=\"乙\"></a></figure>", html);
        Assert.DoesNotContain("<p><figure", html);
    }

    [Fact]
    public void Gallery_without_images_is_left_untouched()
    {
        Assert.Contains("<div class=\"gallery\"><p>只有文字</p>", markdown.Render("::: gallery\n只有文字\n:::"));
    }

    [Fact]
    public void Standalone_image_with_title_gets_a_caption_and_one_without_does_not()
    {
        var html = markdown.Render("段落\n\n![示意](https://example.com/a.png \"這是圖說\")\n\n![無圖說](https://example.com/b.png)\n\n結尾");
        // 帶 title 的圖片成為圖說 figure，沒有 title 的留在段落裡；兩者都由燈箱錨點包住。
        Assert.Contains("<figure class=\"content-figure\"><a class=\"img-lightbox\" href=\"#lb-", html);
        Assert.Contains("<img loading=\"lazy\" decoding=\"async\" src=\"https://example.com/a.png\" alt=\"示意\" title=\"這是圖說\"></a><figcaption>這是圖說</figcaption></figure>", html);
        Assert.Contains("<p><a class=\"img-lightbox\" href=\"#lb-", html);
        Assert.Contains("<img loading=\"lazy\" decoding=\"async\" src=\"https://example.com/b.png\" alt=\"無圖說\"></a></p>", html);
        Assert.Equal(1, CountOccurrences(html, "content-figure"));
    }

    [Fact]
    public void Details_with_a_nested_container_keep_everything_after_it()
    {
        var html = markdown.Render(":::: details 展開\n::: note\n提示\n:::\n\n結尾段落\n::::");
        Assert.Contains("<details class=\"details-block\"><summary>展開</summary><div class=\"note\">", html);
        Assert.Contains("<p>結尾段落</p></details>", html);
    }

    [Fact]
    public void Details_starting_with_a_video_embed_keep_the_embed()
    {
        var html = markdown.Render("::: details\nhttps://youtu.be/dQw4w9WgXcQ\n\n說明\n:::");
        Assert.Contains("<summary>詳細內容</summary><iframe class=\"video-embed\" src=\"https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ\"", html);
        Assert.Contains("<p>說明</p>", html);
        Assert.DoesNotContain("EMBED-ORIGAMI-", html);
    }

    [Fact]
    public void Fenced_code_keeps_its_language_class()
    {
        // 服務端已著色的語言（如 csharp）由 CodeHighlighter 改寫成 figure；其餘語言保留 language- class，
        // 交給前端漸進增強（prose.js 載入 highlight.js）。
        Assert.DoesNotContain("<pre><code class=\"language-csharp\">", markdown.Render("```csharp\nvar x = 1;\n```"));
        Assert.Contains("<pre><code class=\"language-elixir\">", markdown.Render("```elixir\nx = 1\n```"));
        Assert.Contains("<pre><code>", markdown.Render("```\n純文字\n```"));
    }

    [Fact]
    public void Button_links_render_inside_a_buttons_container()
    {
        var html = markdown.Render("::: buttons\n[主要](https://a.example)\n[次要](/posts/hello)\n:::");
        Assert.Contains("<div class=\"buttons\"><p><a href=\"https://a.example\">主要</a>", html);
        Assert.Contains("<a href=\"/posts/hello\">次要</a></p>", html);
    }

    [Fact]
    public void Titles_and_captions_cannot_inject_markup()
    {
        var html = markdown.Render(
            ":::: card <script>alert(1)</script>\n" +
            "::: tab <img src=x onerror=alert(2)>\n:::\n" +
            "::: tab 第二\n乙\n:::\n::::\n\n" +
            "![" + "<b>x</b>" + "](https://example.com/c.png \"<img src=x onerror=alert(3)>\")\n\n" +
            "::: gallery\n![<i>y</i>](https://example.com/d.png \"<svg onload=alert(4)>\")\n:::");
        var document = new HtmlParser().ParseDocument(html);
        Assert.Empty(document.QuerySelectorAll("script, [onerror], [onload], b, i"));
        Assert.Equal("<img src=x onerror=alert(3)>", document.QuerySelector(".content-figure figcaption")!.TextContent);
        Assert.Equal("<svg onload=alert(4)>", document.QuerySelector(".gallery-item figcaption")!.TextContent);
        Assert.Equal("<img src=x onerror=alert(2)>", document.QuerySelector(".tab-panel")!.GetAttribute("data-tab-title"));
        Assert.Equal("<b>x</b>", document.QuerySelector(".content-figure img")!.GetAttribute("alt"));
        Assert.Contains("<strong>&lt;script&gt;alert(1)&lt;/script&gt;</strong>", html);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD", "bvid=BV1xx411c7mD&page=1")]
    [InlineData("https://bilibili.com/video/BV1xx411c7mD/", "bvid=BV1xx411c7mD&page=1")]
    [InlineData("https://m.bilibili.com/video/BV1xx411c7mD/?p=2&spm_id_from=333.1007.tianma.1", "bvid=BV1xx411c7mD&page=2")]
    public void Bilibili_links_become_lazy_iframes_with_tracking_parameters_dropped(string link, string query)
    {
        var html = markdown.Render(link);
        Assert.Contains("<iframe class=\"video-embed\" src=\"https://player.bilibili.com/player.html?" + query + "&autoplay=0&danmaku=0\"", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.DoesNotContain("spm_id_from", html);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/av170001")]          // 舊版 av 號不在支援範圍
    [InlineData("https://www.bilibili.com/video/BV1xx411c7m")]      // BV 號長度不足
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD?p=0")] // 分 P 從 1 起算
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD?p=x")]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD/extra")]
    [InlineData("https://b23.tv/BV1xx411c7mD")]                      // 短網址的目標無法離線驗證
    public void Unrecognized_bilibili_links_stay_as_plain_text(string link)
    {
        var html = markdown.Render(link);
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains(link, html);
    }

    [Fact]
    public void Bilibili_embed_is_not_replaced_when_mentioned_inline()
    {
        Assert.DoesNotContain("<iframe", markdown.Render("請看 https://www.bilibili.com/video/BV1xx411c7mD 這部影片"));
    }
}
