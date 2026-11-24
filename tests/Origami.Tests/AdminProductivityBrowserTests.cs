namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AdminProductivityBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Command_palette_searches_content_and_checklist_dismissal_persists()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        var seeded = await actor.Content.SaveAsync(NewContent("命令面板唯一搜尋文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(environment: "Production");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge",
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);

        await page.GotoAsync("/account/login");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.Keyboard.PressAsync("Control+k");
        var dialog = page.Locator("#admin-palette");
        var input = dialog.GetByRole(AriaRole.Combobox);
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(input).ToBeFocusedAsync();
        await input.FillAsync("媒體庫");
        await Expect(dialog.GetByRole(AriaRole.Option)).ToHaveCountAsync(1);
        await input.PressAsync("Enter");
        await page.WaitForURLAsync("**/admin/media");

        await page.Keyboard.PressAsync("Control+k");
        await Expect(dialog).ToBeVisibleAsync();
        await input.FillAsync("命令面板唯一搜尋");
        await Expect(dialog.GetByRole(AriaRole.Option).Filter(new() { HasText = seeded.Title })).ToHaveCountAsync(1);
        await dialog.GetByRole(AriaRole.Option).Filter(new() { HasText = seeded.Title }).ClickAsync();
        await page.WaitForURLAsync("**/admin/content/" + seeded.Id);

        await page.GotoAsync("/admin");
        await page.EvaluateAsync("() => localStorage.removeItem('origami.setup-checklist')");
        await page.ReloadAsync();
        var checklist = page.Locator(".setup-checklist");
        await Expect(checklist).ToBeVisibleAsync();
        await checklist.GetByRole(AriaRole.Button, new() { Name = "稍後再說", Exact = true }).ClickAsync();
        await Expect(checklist).ToHaveCountAsync(0);
        await page.ReloadAsync();
        await Expect(checklist).ToHaveCountAsync(0);
        Assert.Empty(pageErrors);
    }

    [PostgresFact]
    public async Task Site_deletion_requires_confirmation_and_removes_the_site_in_browser()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        var created = await actor.Sites.CreateSiteAsync(new SiteCreateInput
        {
            Name = "待刪站點", Slug = "delete-me", HostName = "delete-me.example.test"
        });
        using (var siteActor = await App.AdministratorAsync(created.Id))
            await siteActor.Content.SaveAsync(NewContent("隨站點刪除的文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        var deletedRoot = Path.Combine(site.StorageRoot, "sites", created.Id.ToString("N"));
        var images = Path.Combine(deletedRoot, "images");
        Directory.CreateDirectory(images);
        await File.WriteAllTextAsync(Path.Combine(images, "delete-check.txt"), "isolated test media");
        await site.StartAsync(environment: "Production");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);

        await page.GotoAsync("/account/login");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.GotoAsync("/admin/sites");
        var row = page.Locator("tbody tr").Filter(new() { HasText = "待刪站點" });
        await Expect(row).ToHaveCountAsync(1);
        await row.GetByRole(AriaRole.Button, new() { Name = "刪除", Exact = true }).ClickAsync();
        await page.Locator("#delete-site-confirm").FillAsync("wrong-confirmation");
        await page.GetByRole(AriaRole.Button, new() { Name = "永久刪除站點", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("代稱不符合");
        await Expect(row).ToHaveCountAsync(1);
        await using (var db = await App.CreateContextAsync())
            Assert.True(await db.Contents.IgnoreQueryFilters().AnyAsync(x => x.SiteId == created.Id));

        await page.Locator("#delete-site-confirm").FillAsync(created.Slug);
        await page.GetByRole(AriaRole.Button, new() { Name = "永久刪除站點", Exact = true }).ClickAsync();
        await Expect(row).ToHaveCountAsync(0);
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已永久刪除");
        await using (var verify = await App.CreateContextAsync())
        {
            Assert.False(await verify.Sites.AnyAsync(x => x.Id == created.Id));
            Assert.False(await verify.Contents.IgnoreQueryFilters().AnyAsync(x => x.SiteId == created.Id));
            Assert.True(await verify.Sites.AnyAsync(x => x.Id == SiteDefaults.DefaultSiteId));
        }
        Assert.False(Directory.Exists(deletedRoot));
        Assert.Empty(pageErrors);
    }
}
