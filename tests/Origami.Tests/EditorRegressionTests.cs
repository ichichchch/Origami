namespace Origami.Tests;

/// <summary>
/// Browser regressions for the content editor: a share link must not take over the concurrency
/// version of another tab, and a changed schedule counts as an unsaved change.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EditorRegressionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Generating_a_preview_link_never_hides_a_change_of_another_tab()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        Guid contentId;
        using (var actor = await App.AdministratorAsync())
        {
            var saved = await actor.Content.SaveAsync(NewContent("原始標題"));
            contentId = saved.Id!.Value;
        }

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var first = await context.NewPageAsync();
        await LoginAsync(first, password);
        var second = await context.NewPageAsync();
        await LoginAsync(second, password);
        var path = "/admin/content/" + contentId;
        await VisitAsync(first, path);
        await VisitAsync(second, path);
        await Expect(first.Locator("#content-title")).ToHaveValueAsync("原始標題");
        await Expect(second.Locator("#content-title")).ToHaveValueAsync("原始標題");

        // The first tab publishes a newer title.
        await first.Locator("#content-title").FillAsync("較新的標題");
        await first.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(first.Locator(".notice-success")).ToContainTextAsync("已儲存為草稿");

        // The second tab creates a share link and then saves: it must report the conflict
        // instead of overwriting the newer title of the first tab.
        await second.GetByRole(AriaRole.Button, new() { Name = "產生預覽連結", Exact = true }).ClickAsync();
        await Expect(second.Locator(".notice-success")).ToContainTextAsync("預覽連結");
        await second.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(second.GetByRole(AriaRole.Alert)).ToContainTextAsync("另一個分頁");

        using var freshActor = await App.AdministratorAsync();
        Assert.Equal("較新的標題", (await freshActor.Content.ListAdminAsync(ContentKind.Post)).Items.Single().Title);
    }

    [PostgresFact]
    public async Task A_changed_schedule_is_an_unsaved_change_and_reaches_the_database()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        Guid contentId;
        using (var actor = await App.AdministratorAsync())
        {
            var saved = await actor.Content.SaveAsync(NewContent("排程草稿"));
            saved.PublishAt = DateTimeOffset.UtcNow.AddHours(2);
            await actor.Content.UpdateAsync(saved);
            contentId = saved.Id!.Value;
        }

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        await LoginAsync(page, password);
        await VisitAsync(page, "/admin/content/" + contentId);
        await Expect(page.Locator(".save-indicator")).ToContainTextAsync("已載入儲存版本");
        await Expect(page.Locator("#content-schedule")).Not.ToHaveValueAsync("");

        // Clearing the schedule is an edit: the editor says so before anything is stored.
        await page.Locator("#content-schedule").FillAsync("");
        await Expect(page.Locator(".save-indicator.unsaved")).ToContainTextAsync("有未儲存的變更");
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存為草稿");

        using var freshActor = await App.AdministratorAsync();
        Assert.Null((await freshActor.Content.GetEditAsync(contentId)).PublishAt);
    }

    private static async Task LoginAsync(IPage page, string password)
    {
        await VisitAsync(page, "/account/login");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static Task<IResponse?> VisitAsync(IPage page, string path) => page.GotoAsync(path, new() { WaitUntil = WaitUntilState.NetworkIdle });
    private static string BrowserChannel() => Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge";
}
