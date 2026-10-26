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

    private static async Task SaveScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name), FullPage = true });
    }
}
