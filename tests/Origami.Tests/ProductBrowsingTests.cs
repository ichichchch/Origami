namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProductBrowsingTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Empty_site_leads_to_management_and_status_filters_match_visible_content()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Link, new() { Name = "開始管理內容", Exact = true }).ClickAsync();
        await Expect(page.Locator(".auth-card")).ToBeVisibleAsync();
        await page.GetByLabel("帳號", new() { Exact = true }).FillAsync("test-admin");
        await page.GetByLabel("密碼", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await Expect(page.Locator(".getting-started")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "查看 AI 功能狀態" })).ToBeVisibleAsync();
        using (var actor = await App.AdministratorAsync())
        {
            await actor.Content.SaveAsync(NewContent("仍在撰寫的文章"));
            await actor.Content.SaveAsync(NewContent("已公開的文章", status: ContentStatus.Published));
            await actor.Content.SaveAsync(NewContent("不混入的頁面", ContentKind.Page, ContentStatus.Published));
        }
        await page.ReloadAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
        await page.GetByRole(AriaRole.Button, new() { Name = "草稿 1", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("tbody")).ToContainTextAsync("仍在撰寫的文章");
        await Expect(page.Locator("tbody").GetByRole(AriaRole.Link, new() { Name = "檢視", Exact = false })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "已發布 1", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody")).ToContainTextAsync("已公開的文章");
        await Expect(page.Locator("tbody").GetByRole(AriaRole.Link, new() { Name = "檢視", Exact = false })).ToHaveCountAsync(1);
        await page.GetByRole(AriaRole.Button, new() { Name = "全部 2", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
        await page.Locator(".admin-nav a[href='/admin/pages']").ClickAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("tbody")).ToContainTextAsync("不混入的頁面");
        await page.GetByRole(AriaRole.Button, new() { Name = "草稿 0", Exact = true }).ClickAsync();
        await Expect(page.Locator(".empty-state")).ToContainTextAsync("目前沒有草稿");
        await page.Locator(".admin-nav a[href='/admin/posts']").ClickAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
        await page.SetViewportSizeAsync(390, 844);
        await SaveScreenshotAsync(page, "content-filters-mobile.png");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"),
            await page.EvaluateAsync<string>("JSON.stringify({viewport:innerWidth,width:document.documentElement.scrollWidth,overflow:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth).map(e=>e.tagName+'.'+e.className)})"));
    }

    [PostgresFact]
    public async Task Readers_can_follow_taxonomies_and_pagination_without_exposing_drafts()
    {
        using var admin = await App.AdministratorAsync();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = "旅行筆記", Slug = "travel-notes" });
        var tag = await admin.Taxonomy.SaveAsync(TaxonomyKind.Tag, new() { Name = "山林", Slug = "mountains" });
        for (var i = 0; i <= Limits.PageSize; i++)
        {
            var post = NewContent("旅行文章 " + i, status: ContentStatus.Published);
            post.CategoryIds = [category.Id!.Value];
            post.TagIds = [tag.Id!.Value];
            post.Markdown = "這是 **旅途** 的摘要。\n\n<script>alert('unsafe')</script>";
            await admin.Content.SaveAsync(post);
        }
        var hidden = NewContent("不可洩漏的草稿");
        hidden.CategoryIds = [category.Id!.Value];
        hidden.TagIds = [tag.Id!.Value];
        await admin.Content.SaveAsync(hidden);
        var longCategory = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new() { Name = new string('W', Limits.TaxonomyName) });
        var other = NewContent("其他主題", status: ContentStatus.Published);
        other.CategoryIds = [longCategory.Id!.Value];
        await admin.Content.SaveAsync(other);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString(), ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Navigation, new() { Name = "常用分類" }).GetByRole(AriaRole.Link, new() { Name = "旅行筆記 11" }).ClickAsync();
        await Expect(page.Locator(".post-preview")).ToHaveCountAsync(Limits.PageSize);
        await Expect(page.Locator(".post-list")).Not.ToContainTextAsync("不可洩漏的草稿");
        await Expect(page.Locator(".post-list")).Not.ToContainTextAsync("其他主題");
        await Expect(page.Locator(".post-summary").First).ToContainTextAsync("這是 旅途 的摘要。");
        await Expect(page.Locator(".post-list script")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new() { Name = "下一頁", Exact = true }).ClickAsync();
        await Expect(page.Locator(".post-preview")).ToHaveCountAsync(1);
        Assert.Contains("category=travel-notes", page.Url);
        await page.Locator(".post-preview h2 a").ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "# 山林", Exact = true }).ClickAsync();
        await Expect(page.Locator("h1")).ToContainTextAsync("山林");
        Assert.Contains("tag=mountains", page.Url);
        await SaveScreenshotAsync(page, "public-taxonomy-mobile.png");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"),
            await page.EvaluateAsync<string>("JSON.stringify({viewport:innerWidth,width:document.documentElement.scrollWidth,overflow:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth).map(e=>e.tagName+'.'+e.className)})"));
        using var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?category=missing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?category=travel-notes&page=999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/?tag=%2F%2F%2F")).StatusCode);
    }

    [PostgresFact]
    public async Task Static_home_page_moves_the_article_list_to_posts_and_settings_bound_sizes()
    {
        using var admin = await App.AdministratorAsync();
        static ContentEdit PageInput(string title, string slug) => new()
        { Title = title, Slug = slug, Kind = ContentKind.Page, Status = ContentStatus.Published, Markdown = "頁面內容。" };
        static ContentEdit Post(string title, string slug) => new()
        { Title = title, Slug = slug, Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "文章內容。" };
        var home = await admin.Content.CreateAsync(ContentKind.Page, PageInput("歡迎來到本站", "welcome"));
        await admin.Content.SaveAsync(Post("第一篇文章", "first-post"));
        await admin.Content.SaveAsync(Post("第二篇文章", "second-post"));
        await admin.Content.SaveAsync(Post("第三篇文章", "third-post"));
        var secret = PageInput("私密頁面", "secret-home");
        secret.IsPrivate = true;
        var secretSaved = await admin.Content.CreateAsync(ContentKind.Page, secret);

        // 私密頁面不得設為首頁；閱讀相關數量也受上限約束。
        var settings = await admin.Settings.GetAsync();
        settings.HomePageContentId = secretSaved.Id;
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));
        settings = await admin.Settings.GetAsync();
        settings.HomePageContentId = home.Id;
        settings.PostsPerPage = 2;
        settings.FeedItems = 1;
        settings.PostsPerPage = 51; // 比上限多 1
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));
        settings = await admin.Settings.GetAsync();
        settings.HomePageContentId = home.Id;
        settings.PostsPerPage = 2;
        settings.FeedItems = 1;
        await admin.Settings.SaveAsync(settings);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var fetchedHome = await admin.Content.GetPublishedPageAsync(home.Id!.Value);
        Assert.NotNull(fetchedHome);
        var homeResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        var index = WebUtility.HtmlDecode(await homeResponse.Content.ReadAsStringAsync());
        Assert.Contains("歡迎來到本站", index);
        Assert.Contains("前往文章列表", index);
        Assert.DoesNotContain("第一篇文章", index);
        var postsResponse = await client.GetAsync("/posts");
        var posts = WebUtility.HtmlDecode(await postsResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, postsResponse.StatusCode);
        Assert.Contains("第二篇文章", posts);
        Assert.Contains("第 1 / 2 頁", posts);
        var feed = await (await client.GetAsync("/feed")).Content.ReadAsStringAsync();
        Assert.Equal(1, feed.Split("<item>").Length - 1);

        // 撤回靜態首頁後，"/" 退回文章列表，/posts 也提供該列表。
        var withdrawn = await admin.Content.GetEditAsync(home.Id!.Value);
        withdrawn.Status = ContentStatus.Draft;
        await admin.Content.UpdateAsync(withdrawn);
        var fallback = WebUtility.HtmlDecode(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("post-list", fallback);
        Assert.Contains("篇文章", fallback);
        Assert.DoesNotContain("歡迎來到本站", fallback);
        var redirect = await client.GetAsync("/posts");
        // 設定仍指向某頁面，因此 /posts 繼續提供列表，與後備首頁並存。
        Assert.Equal(HttpStatusCode.OK, redirect.StatusCode);
        var cleared = await admin.Settings.GetAsync();
        cleared.HomePageContentId = null;
        await admin.Settings.SaveAsync(cleared);
        var unsetRedirect = await client.GetAsync("/posts");
        Assert.Equal(HttpStatusCode.Redirect, unsetRedirect.StatusCode);
        Assert.Equal("/", unsetRedirect.Headers.Location?.ToString());
    }

    private static async Task SaveScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name), FullPage = true });
    }

    [PostgresFact]
    public async Task Readers_get_neighbours_related_posts_and_month_archives()
    {
        using var admin = await App.AdministratorAsync();
        static ContentEdit Published(string title, string slug) => new()
        { Title = title, Slug = slug, Kind = ContentKind.Post, Status = ContentStatus.Published, Markdown = "關於養蜂與自然觀察的長篇筆記。" };
        await admin.Content.SaveAsync(Published("最早的養蜂筆記", "bee-early"));
        await admin.Content.SaveAsync(Published("居中的養蜂筆記", "bee-middle"));
        await admin.Content.SaveAsync(Published("最新的養蜂筆記", "bee-late"));
        var secret = Published("私密不外流", "secret-archive");
        secret.IsPrivate = true;
        await admin.Content.SaveAsync(secret);

        // 把每篇文章錨定在 15 日，使其各自落在不同且可預期的月份。
        var now = DateTimeOffset.UtcNow;
        var anchor = new DateTimeOffset(now.Year, now.Month, 15, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset middlePublished;
        await using (var db = await App.CreateContextAsync())
        {
            foreach (var (slug, publishedAt) in new[]
            {
                ("bee-early", anchor.AddMonths(-2)),
                ("bee-middle", anchor.AddMonths(-1)),
                ("bee-late", anchor),
                ("secret-archive", anchor),
            })
                await db.Contents.Where(x => x.Slug == slug).ExecuteUpdateAsync(s => s.SetProperty(x => x.PublishedAt, publishedAt));
            middlePublished = await db.Contents.Where(x => x.Slug == "bee-middle").Select(x => x.PublishedAt!.Value).SingleAsync();
        }
        await App.Worker.ProcessPendingAsync();

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        // 中間那篇文章會在公開時間軸上連向前後兩篇。
        var middle = WebUtility.HtmlDecode(await (await client.GetAsync("/posts/bee-middle")).Content.ReadAsStringAsync());
        Assert.Contains("/posts/bee-early", middle);
        Assert.Contains("/posts/bee-late", middle);
        Assert.Contains("上一篇", middle);
        Assert.Contains("下一篇 →", middle);
        // 最新一篇沒有「下一篇」；其相關文章區段會列出另一篇已發布文章。
        var latest = WebUtility.HtmlDecode(await (await client.GetAsync("/posts/bee-late")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("下一篇 →", latest);
        var relatedIndex = latest.IndexOf("相關文章", StringComparison.Ordinal);
        Assert.True(relatedIndex >= 0, "related posts section is present");
        Assert.Contains("養蜂", latest[relatedIndex..]);

        // 歸檔總覽列出各月份；月份頁面只列出該月的公開文章。
        var archives = WebUtility.HtmlDecode(await (await client.GetAsync("/archives")).Content.ReadAsStringAsync());
        Assert.Contains("文章歸檔", archives);
        Assert.Contains($"{middlePublished.Year} 年 {middlePublished.Month} 月", archives);
        var monthPage = WebUtility.HtmlDecode(await (await client.GetAsync($"/archives/{middlePublished.Year}/{middlePublished.Month}")).Content.ReadAsStringAsync());
        Assert.Contains("居中的養蜂筆記", monthPage);
        Assert.DoesNotContain("最早的養蜂筆記", monthPage);
        var latestMonthPage = WebUtility.HtmlDecode(await (await client.GetAsync($"/archives/{now.Year}/{now.Month}")).Content.ReadAsStringAsync());
        Assert.Contains("最新的養蜂筆記", latestMonthPage);
        Assert.DoesNotContain("私密不外流", latestMonthPage);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/archives/2026/13")).StatusCode);
    }
}
