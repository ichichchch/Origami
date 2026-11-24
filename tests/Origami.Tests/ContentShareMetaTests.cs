namespace Origami.Tests;

/// <summary>文章頁的社群分享中繼資料、譯本替代網址、內容語言標記與分享列。</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentShareMetaTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Detail_page_emits_twitter_card_share_links_and_default_language()
    {
        using var admin = await App.AdministratorAsync();
        var plain = await admin.Content.SaveAsync(NewContent("分享中繼文章", status: ContentStatus.Published));
        var withImage = NewContent("帶圖分享文章", status: ContentStatus.Published);
        withImage.Markdown = "![封面](https://example.com/cover.png)\n\n內文。";
        var imagePost = await admin.Content.SaveAsync(withImage);
        var locked = NewContent("受保護分享文章", status: ContentStatus.Published);
        locked.IsProtected = true;
        locked.NewProtectPassword = "secret-pass-123";
        var lockedPost = await admin.Content.SaveAsync(locked);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var plainResponse = await client.GetAsync("/posts/" + plain.Slug);
        var html = WebUtility.HtmlDecode(await plainResponse.Content.ReadAsStringAsync());
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary\"", html);
        Assert.Contains("<meta name=\"twitter:title\" content=\"分享中繼文章\"", html);
        Assert.Contains("<meta name=\"twitter:description\" content=\"", html);
        Assert.DoesNotContain("twitter:image", html, StringComparison.Ordinal);
        Assert.Contains("class=\"share-links\"", html);
        Assert.Contains("https://www.facebook.com/sharer/sharer.php?u=", html);
        Assert.Contains("https://line.me/R/msg/text/?", html);
        Assert.Contains("https://twitter.com/intent/tweet?url=", html);
        Assert.Contains("mailto:?subject=", html);
        Assert.Contains("&body=http", html);
        Assert.Contains("data-share-url=", html);
        Assert.Contains("rel=\"noopener nofollow\"", html);
        // 未標記語言的內容：結構化資料退回產品預設語言。
        Assert.Contains("\"inLanguage\":\"zh-Hant\"", html);
        Assert.DoesNotContain("hreflang=", html, StringComparison.Ordinal);

        using var imageResponse = await client.GetAsync("/posts/" + imagePost.Slug);
        var imageHtml = WebUtility.HtmlDecode(await imageResponse.Content.ReadAsStringAsync());
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", imageHtml);
        Assert.Contains("<meta name=\"twitter:image\" content=\"https://example.com/cover.png\"", imageHtml);

        using var lockedResponse = await client.GetAsync("/posts/" + lockedPost.Slug);
        var lockedHtml = await lockedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("twitter:card", lockedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("share-links", lockedHtml, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Translations_declare_each_other_with_canonical_urls_and_own_language()
    {
        using var admin = await App.AdministratorAsync();
        var zh = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "中文原文", Language = "zh-Hant", Status = ContentStatus.Published
        });
        var en = await admin.Content.CreateAsync(ContentKind.Post, new ContentEdit
        {
            Title = "English version", Language = "en", Status = ContentStatus.Published,
            LinkTranslationTo = zh.Id
        });

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var zhResponse = await client.GetAsync("/posts/" + zh.Slug);
        var zhHtml = await zhResponse.Content.ReadAsStringAsync();
        Assert.Contains("hreflang=\"zh-Hant\" href=\"http", zhHtml, StringComparison.Ordinal);
        Assert.Contains("/posts/" + en.Slug + "\"", zhHtml, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\" href=\"http", zhHtml, StringComparison.Ordinal);
        Assert.Contains("\"inLanguage\":\"zh-Hant\"", zhHtml);

        // 介面語言為繁中時，英文譯本網址會回退顯示同組的繁中版本（既有翻譯組設計）；
        // 以 ?lang=en 明確指定語言後才是英文內容，結構化資料標記英文，x-default 指向產品預設語言的譯本。
        using var enResponse = await client.GetAsync("/posts/" + en.Slug + "?lang=en");
        var enHtml = await enResponse.Content.ReadAsStringAsync();
        Assert.Contains("hreflang=\"en\" href=\"http", enHtml, StringComparison.Ordinal);
        Assert.Contains("/posts/" + Uri.EscapeDataString(zh.Slug ?? "") + "\"", enHtml, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"zh-Hant\" href=\"http", enHtml, StringComparison.Ordinal);
        Assert.Contains("\"inLanguage\":\"en\"", enHtml);
        var xDefault = System.Text.RegularExpressions.Regex.Match(enHtml, "hreflang=\"x-default\" href=\"([^\"]+)\"");
        Assert.True(xDefault.Success, "缺少 x-default 替代網址。");
        Assert.EndsWith("/posts/" + Uri.EscapeDataString(zh.Slug ?? ""), xDefault.Groups[1].Value, StringComparison.Ordinal);

        var zhPage = await admin.Content.CreateAsync(ContentKind.Page, new ContentEdit
        {
            Title = "繁體獨立頁", Slug = "traditional-page", Language = "zh-Hant", Status = ContentStatus.Published
        });
        var enPage = await admin.Content.CreateAsync(ContentKind.Page, new ContentEdit
        {
            Title = "English page", Slug = "english-page", Language = "en", Status = ContentStatus.Published,
            LinkTranslationTo = zhPage.Id
        });
        using var pageResponse = await client.GetAsync("/pages/" + zhPage.Slug);
        var pageHtml = await pageResponse.Content.ReadAsStringAsync();
        Assert.Contains("hreflang=\"zh-Hant\" href=\"http", pageHtml, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\" href=\"http", pageHtml, StringComparison.Ordinal);
        Assert.Contains("/pages/" + enPage.Slug + "\"", pageHtml, StringComparison.Ordinal);
        Assert.Contains("\"inLanguage\":\"zh-Hant\"", pageHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"share-links\"", pageHtml, StringComparison.Ordinal);
    }
}
