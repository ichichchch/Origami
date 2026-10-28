namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PublicSyndicationTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Manual_excerpt_is_trimmed_bounded_and_preferred_over_the_fallback()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("Excerpt post", status: ContentStatus.Published);
        post.Markdown = "本文開頭不應取代手寫摘要。";
        post.Excerpt = "  手寫摘要，前後空白會被修剪。  ";
        var saved = await admin.Content.SaveAsync(post);
        Assert.Equal("手寫摘要，前後空白會被修剪。", saved.Excerpt);
        Assert.Equal("手寫摘要，前後空白會被修剪。", (await admin.Content.GetEditAsync(saved.Id!.Value)).Excerpt);

        // 長度上限與其他設有上限的欄位走同一條驗證路徑。
        var edit = await admin.Content.GetEditAsync(saved.Id!.Value);
        edit.Excerpt = new string('摘', Limits.Excerpt + 1);
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(edit));

        var listed = await admin.Content.ListPublishedAsync();
        var manual = Assert.Single(listed.Items);
        Assert.True(manual.ExcerptIsManual);
        Assert.Equal("手寫摘要，前後空白會被修剪。", manual.Excerpt);

        // 沒有手寫摘要的內容沿用 Markdown 開頭的後備摘要，渲染時才剝除標記。
        var fallback = NewContent("Fallback post", status: ContentStatus.Published);
        fallback.Slug = "fallback-post";
        fallback.Markdown = "這是 **粗體** 開頭。";
        await admin.Content.SaveAsync(fallback);
        var item = (await admin.Content.ListPublishedAsync()).Items.Single(x => x.Slug == "fallback-post");
        Assert.False(item.ExcerptIsManual);
        Assert.Equal("這是 **粗體** 開頭。", item.Excerpt);
    }

    [PostgresFact]
    public async Task Robots_sitemap_and_feed_expose_only_published_state()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "發布", Slug = "released" });
        var post = NewContent("Feed & <b>標題</b>", status: ContentStatus.Published);
        post.Slug = "feed-post";
        post.Excerpt = "手寫摘要，用於 feed。";
        post.CategoryIds = [category.Id!.Value];
        await admin.Content.SaveAsync(post);
        var draft = NewContent("草稿不入公開面");
        draft.Slug = "draft-post";
        await admin.Content.SaveAsync(draft);
        var page = NewContent("關於本站", ContentKind.Page, ContentStatus.Published);
        page.Slug = "about";
        await admin.Content.SaveAsync(page);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        var robots = await client.GetAsync("/robots.txt");
        Assert.True(robots.IsSuccessStatusCode);
        Assert.Equal("text/plain", robots.Content.Headers.ContentType!.MediaType);
        var robotsText = await robots.Content.ReadAsStringAsync();
        Assert.Contains("User-agent: *", robotsText);
        Assert.Contains("Disallow: /admin", robotsText);
        Assert.Contains("Sitemap: ", robotsText);
        Assert.Contains("/sitemap.xml", robotsText);

        var sitemap = await client.GetAsync("/sitemap.xml");
        Assert.True(sitemap.IsSuccessStatusCode);
        Assert.Equal("application/xml", sitemap.Content.Headers.ContentType!.MediaType);
        var xml = await sitemap.Content.ReadAsStringAsync();
        Assert.Contains("http://www.sitemaps.org/schemas/sitemap/0.9", xml);
        Assert.Contains("/posts/feed-post", xml);
        Assert.Contains("/pages/about", xml);
        Assert.Contains("/?category=released", xml);
        Assert.DoesNotContain("draft-post", xml);

        var feed = await client.GetAsync("/feed");
        Assert.True(feed.IsSuccessStatusCode);
        Assert.Equal("application/rss+xml", feed.Content.Headers.ContentType!.MediaType);
        var rss = await feed.Content.ReadAsStringAsync();
        // feed 全文功能在 rss 根元素宣告 content 模組命名空間（xmlns:content="…"），
        // 斷言只比對根元素與版本，不要求 version 屬性後面緊接 ">"。
        Assert.Contains("<rss version=\"2.0\"", rss);
        Assert.Contains("<title>Feed &amp; &lt;b&gt;標題&lt;/b&gt;</title>", rss);
        Assert.Contains("手寫摘要，用於 feed。", rss);
        Assert.DoesNotContain("draft-post", rss);

        // 撤回後，下一次請求起每個機器端介面都不再有該項目。
        var withdraw = await admin.Content.GetEditAsync(post.Id!.Value);
        withdraw.Status = ContentStatus.Draft;
        await admin.Content.UpdateAsync(withdraw);
        Assert.DoesNotContain("/posts/feed-post", await (await client.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync());
        var updatedFeed = await (await client.GetAsync("/feed")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("/posts/feed-post", updatedFeed);
    }

    [PostgresFact]
    public async Task Detail_and_home_pages_render_social_meta_and_canonical_urls()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("Meta 標題", status: ContentStatus.Published);
        post.Slug = "meta-post";
        post.Excerpt = "社群分享用的摘要。";
        post.Markdown = "開頭文字。\n\n![封面圖](/media/cover.png)\n";
        await admin.Content.SaveAsync(post);
        var secret = NewContent("未發布不該有 meta");
        secret.Slug = "secret-post";
        await admin.Content.SaveAsync(secret);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        // Blazor 會把 head 屬性中的非 ASCII 字元編成數值實體；斷言前先解碼。
        var html = WebUtility.HtmlDecode(await (await client.GetAsync("/posts/meta-post")).Content.ReadAsStringAsync());
        Assert.Contains("<meta name=\"description\" content=\"社群分享用的摘要。\" />", html);
        Assert.Contains("<meta property=\"og:title\" content=\"Meta 標題\" />", html);
        Assert.Contains("<meta property=\"og:description\" content=\"社群分享用的摘要。\" />", html);
        Assert.Contains("<meta property=\"og:type\" content=\"article\" />", html);
        Assert.Contains("property=\"article:published_time\"", html);
        Assert.Contains("rel=\"canonical\"", html);
        Assert.Contains("/posts/meta-post", html);
        Assert.Contains("<meta property=\"og:image\" content=\"", html);
        Assert.Contains("/media/cover.png", html);

        var home = WebUtility.HtmlDecode(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("property=\"og:site_name\"", home);
        Assert.Contains("rel=\"canonical\"", home);
        Assert.Contains("type=\"application/rss+xml\"", home);

        // 草稿維持 404，絕不洩漏 meta 標籤。
        var missing = await client.GetAsync("/posts/secret-post");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.DoesNotContain("og:", await missing.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task The_feed_is_bounded_to_the_configured_item_limit()
    {
        using var admin = await App.AdministratorAsync();
        for (var i = 0; i < Limits.FeedItems + 4; i++)
        {
            var post = NewContent("Feed 文章 " + i, status: ContentStatus.Published);
            post.Slug = "feed-cap-" + i;
            await admin.Content.SaveAsync(post);
        }
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var rss = await (await client.GetAsync("/feed")).Content.ReadAsStringAsync();
        Assert.Equal(Limits.FeedItems, rss.Split("<item>").Length - 1);
        // 最新的已發布文章排在頻道開頭。
        Assert.Contains("Feed 文章 " + (Limits.FeedItems + 3), rss);
    }
}
