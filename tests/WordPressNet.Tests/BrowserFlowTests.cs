using System.Net;
using Microsoft.Playwright;
using SkiaSharp;
using WordPressNet.Web.Models;
using static Microsoft.Playwright.Assertions;

namespace WordPressNet.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BrowserFlowTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Administrator_can_complete_the_publishing_loop_in_a_real_browser()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString(), ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await context.NewPageAsync();
        var consoleMessages = new List<string>();
        page.Console += (_, message) => consoleMessages.Add(message.Type + ": " + message.Text);
        await LoginAsync(page, password);
        await VisitAsync(page, "/admin/settings");
        await page.Locator("#site-name").FillAsync("旅程筆記");
        await page.Locator("#site-description").FillAsync("紀錄每個值得分享的想法。");
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存設定", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("網站設定已儲存");

        await VisitAsync(page, "/admin/taxonomies");
        await page.Locator("#taxonomy-name").FillAsync("旅行");
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存分類", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存分類");
        await page.GetByRole(AriaRole.Button, new() { Name = "標籤", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "新增標籤", Exact = true })).ToBeVisibleAsync();
        await page.Locator("#taxonomy-name").FillAsync("日常");
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存標籤", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存標籤");

        await VisitAsync(page, "/admin/media");
        await page.Locator("#media-upload").SetInputFilesAsync(new FilePayload { Name = "journey.png", MimeType = "image/png", Buffer = ImageBytes() });
        await Expect(page.Locator("#uploaded-markdown")).ToBeVisibleAsync();
        var imageMarkdown = await page.Locator("#uploaded-markdown").InputValueAsync();
        Assert.Contains("/media/", imageMarkdown);

        await VisitAsync(page, "/admin/posts/new");
        await page.Locator("#content-title").FillAsync("第一篇旅程");
        await page.Locator("#content-slug").FillAsync("first-journey");
        await page.Locator("#content-markdown").FillAsync("## 出發\n\n**值得記錄**的一天。");
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "旅行", Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "日常", Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "插入圖片", Exact = true }).ClickAsync();
        await page.Locator(".media-choice").Filter(new() { HasText = "journey.png" }).ClickAsync();
        await Expect(page.Locator("#content-markdown")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(imageMarkdown)));
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/content/*");
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("內容已建立為草稿");
        var editUrl = page.Url;
        using var publicClient = site.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/posts/first-journey")).StatusCode);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var publishButton = page.GetByRole(AriaRole.Button, new() { Name = "儲存並發布", Exact = true });
        Assert.Equal(1, await publishButton.CountAsync());
        Assert.True(await publishButton.IsEnabledAsync(), "publish button must be enabled before the click");
        await publishButton.ClickAsync();
        try
        {
            await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存並公開", new() { Timeout = 5000 });
        }
        catch (Exception error)
        {
            var diagnostics = "fieldsetDisabled=" + await page.Locator("fieldset").First.GetAttributeAsync("disabled")
                + "; publishDisabled=" + await publishButton.GetAttributeAsync("disabled")
                + "; notices=" + await page.Locator(".notice").AllInnerTextsAsync()
                + "; url=" + page.Url
                + "; console=" + string.Join(" | ", consoleMessages);
            throw new InvalidOperationException(diagnostics, error);
        }
        var firstPublished = await publicClient.GetStringAsync("/posts/first-journey");
        Assert.Contains("/media/", firstPublished);
        await SaveScreenshotAsync(page, "admin-editor.png");
        using (var actor = await App.AdministratorAsync())
        {
            var item = await actor.Content.GetPublishedAsync(ContentKind.Post, "first-journey");
            Assert.Equal(["旅行"], item!.Categories);
            Assert.Equal(["日常"], item.Tags);
        }

        await VisitAsync(page, "/admin/pages/new");
        await page.Locator("#content-title").FillAsync("關於這裡");
        await page.Locator("#content-slug").FillAsync("about");
        await page.Locator("#content-markdown").FillAsync("這是我的寫作空間。");
        Assert.Equal(0, await page.GetByRole(AriaRole.Checkbox).CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存並發布", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/content/*");
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("內容已建立並公開");
        var visitor = await context.NewPageAsync();
        await VisitAsync(visitor, "/");
        await Expect(visitor.Locator(".site-brand")).ToHaveTextAsync("旅程筆記");
        await Expect(visitor.GetByRole(AriaRole.Link, new() { Name = "關於這裡", Exact = true })).ToBeVisibleAsync();
        await visitor.SetViewportSizeAsync(390, 844);
        Assert.True(await visitor.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await VisitAsync(visitor, "/posts/first-journey");
        await Expect(visitor.Locator("article img")).ToBeVisibleAsync();
        Assert.True(await visitor.Locator("article img").EvaluateAsync<bool>("img => img.complete && img.naturalWidth > 0"));
        Assert.True(await visitor.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await SaveScreenshotAsync(visitor, "public-mobile.png");

        await VisitAsync(page, editUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存並撤回為草稿", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已撤回為草稿");
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/posts/first-journey")).StatusCode);
        var imagePath = imageMarkdown[(imageMarkdown.IndexOf('(') + 1)..^1];
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync(imagePath)).StatusCode);
        await VisitAsync(page, "/admin/posts");
        await page.GetByRole(AriaRole.Button, new() { Name = "刪除 第一篇旅程" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alertdialog)).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "取消", Exact = true }).ClickAsync();
        await Expect(page.Locator(".content-title")).ToHaveTextAsync("第一篇旅程");
        await page.GetByRole(AriaRole.Button, new() { Name = "刪除 第一篇旅程" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "確認永久刪除", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已永久刪除內容");
        await page.GetByRole(AriaRole.Button, new() { Name = "登出", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(site.Address.ToString());
        await VisitAsync(page, "/admin/posts");
        Assert.Contains("/account/login", page.Url);
    }

    [PostgresFact]
    public async Task Long_body_conflicting_tabs_unsaved_warning_and_logout_revoke_old_circuit()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var first = await context.NewPageAsync();
        await LoginAsync(first, password);
        await VisitAsync(first, "/admin/posts/new");
        await first.Locator("#content-title").FillAsync("Long body");
        await first.Locator("#content-markdown").FillAsync(new string('文', Limits.Body));
        await first.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await first.WaitForURLAsync("**/admin/content/*");
        await Expect(first.Locator(".notice-success")).ToContainTextAsync("內容已建立為草稿");
        var second = await context.NewPageAsync();
        await VisitAsync(second, first.Url);
        // The interactive circuit of the second tab must be live and hold the version it
        // loaded. The preview button only answers to the server, so it proves both.
        await second.GetByRole(AriaRole.Button, new() { Name = "預覽", Exact = true }).ClickAsync();
        await Expect(second.Locator(".editor-preview")).ToBeVisibleAsync();
        await Expect(second.Locator("#content-title")).ToHaveValueAsync("Long body");
        await first.Locator("#content-title").FillAsync("Newer title");
        await first.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(first.Locator(".notice-success")).ToContainTextAsync("已儲存為草稿");
        await second.Locator("#content-title").FillAsync("Stale title");
        await second.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(second.GetByRole(AriaRole.Alert)).ToContainTextAsync("另一個分頁");
        await Expect(second.Locator("#content-title")).ToHaveValueAsync("Stale title");        var dialogSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.Dialog += async (_, dialog) =>
        {
            if (dialog.Type == "beforeunload") dialogSeen.TrySetResult();
            await dialog.DismissAsync();
        };
        await second.Locator("a.back-link").ClickAsync();
        await dialogSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("/admin/content/", second.Url);
        await first.GetByRole(AriaRole.Button, new() { Name = "登出", Exact = true }).ClickAsync();
        await first.WaitForURLAsync(site.Address.ToString());
        await second.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(second.GetByRole(AriaRole.Alert)).ToContainTextAsync("登入");
        using var freshActor = await App.AdministratorAsync();
        Assert.Equal("Newer title", (await freshActor.Content.ListAdminAsync(ContentKind.Post)).Items.Single().Title);
    }

    [PostgresFact]
    public async Task Disconnected_editor_warns_before_reload_and_never_claims_unsaved_work_was_saved()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        string editPath;
        using (var actor = await App.AdministratorAsync())
        {
            var saved = await actor.Content.SaveAsync(NewContent("Saved before disconnect"));
            editPath = "/admin/content/" + saved.Id;
        }
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        await LoginAsync(page, password);
        await VisitAsync(page, editPath);
        await page.Locator("#content-title").FillAsync("Not saved before disconnect");
        await Expect(page.Locator(".save-indicator")).ToContainTextAsync("未儲存");
        await site.StopAsync();
        await Expect(page.Locator("#components-reconnect-modal")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(page.Locator("#reconnect-warning")).ToContainTextAsync("連線恢復不代表儲存成功");
        Assert.Equal(0, await page.Locator(".notice-success").CountAsync());
        using (var actor = await App.AdministratorAsync())
            Assert.Equal("Saved before disconnect", (await actor.Content.ListAdminAsync(ContentKind.Post)).Items.Single().Title);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await site.StartAsync(reuseAddress: true);
        // A restarted single-instance server cannot recover the old circuit. The explicit
        // reload path warns about unsaved input and reloads only the persisted version.
        await page.Locator("#components-reload-button").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(page.Locator("#content-title")).ToHaveValueAsync("Saved before disconnect");
        await Expect(page.Locator("#components-reconnect-modal")).Not.ToBeVisibleAsync();
        await page.Locator("#content-title").FillAsync("Saved after reconnect");
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存為草稿");
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
    private static string BrowserChannel() => Environment.GetEnvironmentVariable("WORDPRESSNET_BROWSER_CHANNEL") ?? "msedge";
    private static async Task SaveScreenshotAsync(IPage page, string name)
    {
        var folder = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
        Directory.CreateDirectory(folder);
        await page.ScreenshotAsync(new() { Path = Path.Combine(folder, name), FullPage = true });
    }
    private static byte[] ImageBytes()
    {
        using var bitmap = new SKBitmap(24, 16);
        bitmap.Erase(new SKColor(85, 126, 104));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
