using System.Collections.Concurrent;
using System.Net;
using Microsoft.Playwright;
using SkiaSharp;
using Origami.Web.Models;
using Origami.Web.Services;
using static Microsoft.Playwright.Assertions;

namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BrowserFlowTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Production_build_loads_assets_and_persists_interactive_edits()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        ContentEdit seeded;
        using (var actor = await App.AdministratorAsync())
            seeded = await actor.Content.SaveAsync(NewContent("Production original title"));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(environment: "Production");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        var consoleMessages = new ConcurrentQueue<string>();
        var pageErrors = new ConcurrentQueue<string>();
        var resourceResponses = new ConcurrentQueue<(string Url, int Status)>();
        var failedRequests = new ConcurrentQueue<string>();
        page.Console += (_, message) => consoleMessages.Enqueue(message.Type + ": " + message.Text);
        page.PageError += (_, error) => pageErrors.Enqueue(error);
        page.Response += (_, response) =>
        {
            if (response.Request.ResourceType is "script" or "stylesheet") resourceResponses.Enqueue((response.Url, response.Status));
        };
        page.RequestFailed += (_, request) =>
        {
            if (request.ResourceType is "script" or "stylesheet") failedRequests.Enqueue(request.Url + ": " + request.Failure);
        };

        try
        {
            // Use normal HTTPS form login; document load is not a circuit-ready signal.
            await page.GotoAsync("/account/login");
            await page.Locator("#username").FillAsync("test-admin");
            await page.Locator("#password").FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/admin/posts");
            await page.GotoAsync("/admin/content/" + seeded.Id);

            var assetUrls = await page.Locator("link[rel='stylesheet'], script[src]")
                .EvaluateAllAsync<string[]>("elements => elements.map(element => element.href || element.src)");
            Assert.Contains(assetUrls, url => url.Contains("/Origami.Web.", StringComparison.Ordinal) && url.EndsWith(".styles.css", StringComparison.Ordinal));
            Assert.Contains(assetUrls, url => url.Contains("/_framework/blazor.web.", StringComparison.Ordinal));
            Assert.Contains(assetUrls, url => url.Contains("/Components/Layout/ReconnectModal.", StringComparison.Ordinal) && url.EndsWith(".razor.js", StringComparison.Ordinal));
            foreach (var url in assetUrls)
                Assert.Contains(resourceResponses, response => response.Url == url && response.Status is >= 200 and < 300);

            // A prerendered button can accept a click before its handler attaches.
            // Retry only this read-only probe; the server must return the seeded body.
            for (var attempt = 0; ; attempt++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "預覽", Exact = true }).ClickAsync();
                try
                {
                    await Expect(page.Locator(".editor-preview")).ToHaveTextAsync(seeded.Markdown, new() { Timeout = 1000 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 4)
                {
                    // The final failed assertion propagates with the diagnostics below.
                }
            }
            await Expect(page.Locator("#content-title")).ToHaveValueAsync(seeded.Title);
            await page.Locator("#content-title").FillAsync("Production saved title");
            await page.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
            await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存為草稿");
            await Expect(page.Locator("#content-title")).ToHaveValueAsync("Production saved title");

            using var freshActor = await App.AdministratorAsync();
            var persisted = await freshActor.Content.GetEditAsync(seeded.Id!.Value);
            Assert.Equal("Production saved title", persisted.Title);
            Assert.Equal(seeded.Markdown, persisted.Markdown);
            Assert.NotEqual(seeded.Version, persisted.Version);
            Assert.All(resourceResponses, response => Assert.True(response.Status is >= 200 and < 300, $"HTTP {response.Status}: {response.Url}"));
            Assert.Empty(failedRequests);
            Assert.Empty(pageErrors);
        }
        catch (Exception error)
        {
            var diagnostics = "url=" + page.Url
                + "; fieldsetDisabled=" + await page.EvaluateAsync<bool?>("() => document.querySelector('fieldset.editor-grid')?.disabled ?? null")
                + "; notices=" + string.Join(" | ", await page.Locator(".notice").AllInnerTextsAsync())
                + "; resources=" + string.Join(" | ", resourceResponses.Select(response => $"{response.Status} {response.Url}"))
                + "; failedRequests=" + string.Join(" | ", failedRequests)
                + "; pageErrors=" + string.Join(" | ", pageErrors)
                + "; console=" + string.Join(" | ", consoleMessages);
            throw new InvalidOperationException(diagnostics, error);
        }
    }

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

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnected_editor_warns_before_reload_and_never_claims_unsaved_work_was_saved(bool reducedMotion)
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
        await page.EmulateMediaAsync(new() { ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference });
        await LoginAsync(page, password);
        await VisitAsync(page, editPath);
        await page.GetByRole(AriaRole.Button, new() { Name = "預覽", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-preview")).ToHaveTextAsync("Test body");
        await Expect(page.Locator("#content-title")).ToHaveValueAsync("Saved before disconnect");
        await page.Locator("#content-title").FillAsync("Not saved before disconnect");
        await Expect(page.Locator(".save-indicator")).ToContainTextAsync("未儲存");
        await site.StopAsync();
        var reconnect = page.Locator("#components-reconnect-modal");
        await Expect(reconnect).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(reconnect).ToHaveCSSAsync("opacity", "1");
        Assert.True(await reconnect.EvaluateAsync<bool>("element => element.matches(':modal')"));
        if (reducedMotion) await Expect(reconnect).ToHaveCSSAsync("animation-name", "none");
        await Expect(page.Locator("#reconnect-warning")).ToContainTextAsync("連線恢復不代表儲存成功");
        await Expect(page.Locator("#components-reload-button")).ToBeVisibleAsync();
        await SaveScreenshotAsync(page, reducedMotion ? "disconnect-reduced-motion.png" : "disconnect-default-motion.png");
        Assert.Equal(0, await page.Locator(".notice-success").CountAsync());
        using (var actor = await App.AdministratorAsync())
            Assert.Equal("Saved before disconnect", (await actor.Content.ListAdminAsync(ContentKind.Post)).Items.Single().Title);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await site.StartAsync(reuseAddress: true);
        // Restarting cannot recover the old circuit, so reload only the persisted version.
        await page.Locator("#components-reload-button").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByRole(AriaRole.Button, new() { Name = "預覽", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-preview")).ToHaveTextAsync("Test body");
        await Expect(page.Locator("#content-title")).ToHaveValueAsync("Saved before disconnect");
        await Expect(reconnect).Not.ToBeVisibleAsync();
        await page.Locator("#content-title").FillAsync("Saved after reconnect");
        await page.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已儲存為草稿");
        using var freshActor = await App.AdministratorAsync();
        Assert.Equal("Saved after reconnect", (await freshActor.Content.ListAdminAsync(ContentKind.Post)).Items.Single().Title);
    }

    [PostgresFact]
    public async Task Long_page_titles_and_site_names_fit_the_mobile_viewport()
    {
        var longTitle = new string('p', 100);
        var longSiteName = new string('s', 100);
        using (var actor = await App.AdministratorAsync())
        {
            await actor.Content.SaveAsync(NewContent(longTitle, ContentKind.Page, ContentStatus.Published));
            var settings = await actor.Settings.GetAsync();
            settings.Name = longSiteName;
            await actor.Settings.SaveAsync(settings);
        }
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString(), ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await VisitAsync(page, "/");
        // The public home is static SSR; the rendered values prove the server data arrived.
        await Expect(page.Locator(".site-nav a").Nth(1)).ToHaveTextAsync(longTitle);
        await Expect(page.Locator(".site-footer span")).ToContainTextAsync(longSiteName);
        var diagnostics = await page.EvaluateAsync<string>("() => {" +
            "const fail = [];" +
            "if (document.documentElement.scrollWidth > window.innerWidth) fail.push('document ' + document.documentElement.scrollWidth + '>' + window.innerWidth);" +
            "for (const el of document.querySelectorAll('.site-header, .site-nav, .site-footer')) {" +
            "  if (el.scrollWidth > el.clientWidth + 1) fail.push(el.className + ' ' + el.scrollWidth + '>' + el.clientWidth);" +
            "}" +
            "return fail.join('; ');}");
        Assert.True(diagnostics.Length == 0, "Horizontal overflow on mobile: " + diagnostics);
        await SaveScreenshotAsync(page, "public-mobile-long-names.png");
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
