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
                    await Expect(page.Locator(".editor-preview")).ToHaveTextAsync(seeded.Markdown!, new() { Timeout = 1000 });
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
        // Taxonomy selectors finish loading over the circuit (WebSocket), after HTTP NetworkIdle;
        // wait for the button to become enabled instead of asserting its immediate state.
        await Expect(publishButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
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
            Assert.Equal([new NavigationItem("旅行", "旅行")], item!.Categories);
            Assert.Equal([new NavigationItem("日常", "日常")], item.Tags);
        }

        await VisitAsync(page, "/admin/pages/new");
        await page.Locator("#content-title").FillAsync("關於這裡");
        await page.Locator("#content-slug").FillAsync("about");
        await page.Locator("#content-markdown").FillAsync("這是我的寫作空間。");
        // Pages never join the article taxonomy flow; the password-protection checkbox is not a taxonomy.
        Assert.Equal(0, await page.Locator(".taxonomy-panel").CountAsync());
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
        await page.GetByRole(AriaRole.Button, new() { Name = "確認移入回收桶", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已移入回收桶");
        await page.GetByRole(AriaRole.Button, new() { Name = "回收桶 1", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "永久刪除 第一篇旅程" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "確認永久刪除", Exact = true }).ClickAsync();
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("已永久刪除內容");
        await page.GetByRole(AriaRole.Button, new() { Name = "登出", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(site.Address.ToString());
        await VisitAsync(page, "/admin/posts");
        Assert.Contains("/account/login", page.Url);
    }

    [PostgresFact]
    public async Task Empty_editor_can_create_taxonomies_upload_and_publish_without_losing_body_in_edge()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = BrowserChannel(), Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);
        await LoginAsync(page, password);
        await VisitAsync(page, "/admin/posts/new");

        const string title = "Inline publishing journey";
        const string slug = "inline-publishing-journey";
        const string body = "## 現場筆記\n\n**本文不可遺失**，尚未儲存。";
        var markdown = page.Locator("#content-markdown");
        var publish = page.GetByRole(AriaRole.Button, new() { Name = "儲存並發布", Exact = true });
        var draft = page.GetByRole(AriaRole.Button, new() { Name = "儲存草稿", Exact = true });
        await Expect(page.Locator("#content-title")).ToHaveValueAsync("");
        await Expect(markdown).ToHaveValueAsync("");
        await page.Locator("#content-title").FillAsync(title);
        await markdown.FillAsync(body);
        // Leave the slug empty, and create all related data through the editor's controls.
        var category = page.Locator(".taxonomy-selector[aria-label='分類']");
        var tag = page.Locator(".taxonomy-selector[aria-label='標籤']");
        await category.GetByLabel("新增分類名稱", new() { Exact = true }).FillAsync("編輯器分類");
        await category.GetByLabel("新增分類名稱", new() { Exact = true }).PressAsync("Enter");
        await category.GetByRole(AriaRole.Button, new() { Name = "建立並選取分類", Exact = true }).ClickAsync();
        await Expect(category.Locator(".notice-success")).ToContainTextAsync("已建立並選取分類");
        await Expect(category.GetByRole(AriaRole.Checkbox, new() { Name = "編輯器分類", Exact = true })).ToBeCheckedAsync();
        await tag.GetByLabel("新增標籤名稱", new() { Exact = true }).FillAsync("編輯器標籤");
        await tag.GetByRole(AriaRole.Button, new() { Name = "建立並選取標籤", Exact = true }).ClickAsync();
        await Expect(tag.Locator(".notice-success")).ToContainTextAsync("已建立並選取標籤");
        await Expect(tag.GetByRole(AriaRole.Checkbox, new() { Name = "編輯器標籤", Exact = true })).ToBeCheckedAsync();
        await Expect(markdown).ToHaveValueAsync(body);

        // A normalized duplicate must not clear either the name, selection or body.
        await category.GetByLabel("新增分類名稱", new() { Exact = true }).FillAsync(" 編輯器分類 ");
        await category.GetByRole(AriaRole.Button, new() { Name = "建立並選取分類", Exact = true }).ClickAsync();
        await Expect(category.GetByRole(AriaRole.Alert)).ToContainTextAsync("已存在");
        await Expect(category.GetByLabel("新增分類名稱", new() { Exact = true })).ToHaveValueAsync(" 編輯器分類 ");
        await Expect(markdown).ToHaveValueAsync(body);
        await category.GetByRole(AriaRole.Button, new() { Name = "刷新分類", Exact = true }).ClickAsync();
        await Expect(category.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await Expect(category.GetByRole(AriaRole.Checkbox, new() { Name = "編輯器分類", Exact = true })).ToBeCheckedAsync();
        await category.GetByLabel("新增分類名稱", new() { Exact = true }).FillAsync("");
        await tag.GetByRole(AriaRole.Button, new() { Name = "刷新標籤", Exact = true }).ClickAsync();
        await Expect(tag.GetByRole(AriaRole.Checkbox, new() { Name = "編輯器標籤", Exact = true })).ToBeCheckedAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "插入圖片", Exact = true }).ClickAsync();
        var picker = page.Locator(".media-picker");
        var upload = picker.Locator("#picker-upload");
        await Expect(picker).ToContainTextAsync("所有圖片皆公開");
        // The inputs must stay inside the original EditForm, without a nested form.
        await Expect(page.Locator("form:has(#content-title) #picker-upload")).ToHaveCountAsync(1);
        await Expect(page.Locator("form:has(#content-title) #new-taxonomy-Category")).ToHaveCountAsync(1);
        await Expect(page.Locator("fieldset.editor-grid form")).ToHaveCountAsync(0);
        await Expect(upload).ToBeEnabledAsync();
        await upload.SetInputFilesAsync(new FilePayload { Name = "inline.png", MimeType = "image/png", Buffer = Encoding.UTF8.GetBytes("not a PNG") });
        await Expect(picker.GetByRole(AriaRole.Alert)).ToContainTextAsync("圖片無法解碼或格式不合法");
        await Expect(picker.Locator(".media-choice")).ToHaveCountAsync(0);
        await Expect(markdown).ToHaveValueAsync(body);
        await Expect(publish).ToBeEnabledAsync();

        const string bodyDuringUpload = body + "\n\n上傳期間繼續寫作。";
        // Hold only this test database's media insert, making the busy-state assertion
        // deterministic without sleeps, browser mocks or bypassing the upload UI.
        await using (var connection = new NpgsqlConnection(App.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand("LOCK TABLE \"MediaAssets\" IN SHARE MODE", connection, transaction);
            await hold.ExecuteNonQueryAsync();
            // Reuse the failed filename to prove the file control can retry it.
            await upload.SetInputFilesAsync(new FilePayload { Name = "inline.png", MimeType = "image/png", Buffer = ImageBytes() });
            await Expect(picker.GetByText("正在上傳：inline.png", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(draft).ToBeDisabledAsync();
            await Expect(publish).ToBeDisabledAsync();
            await Expect(picker.GetByRole(AriaRole.Button, new() { Name = "關閉", Exact = true })).ToBeDisabledAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "插入圖片", Exact = true })).ToBeDisabledAsync();
            await Expect(markdown).ToBeEnabledAsync();
            await markdown.FillAsync(bodyDuringUpload);
            await using var untouched = await App.CreateContextAsync();
            Assert.Empty(await untouched.Contents.ToListAsync());
            await transaction.RollbackAsync();
        }
        await Expect(picker.Locator(".notice-success")).ToContainTextAsync("圖片已上傳");
        await Expect(publish).ToBeEnabledAsync();
        await Expect(picker.GetByRole(AriaRole.Button, new() { Name = "關閉", Exact = true })).ToBeEnabledAsync();
        await Expect(markdown).ToHaveValueAsync(bodyDuringUpload);
        Assert.Equal("/admin/posts/new", new Uri(page.Url).AbsolutePath);
        var choice = picker.Locator(".media-choice").Filter(new() { HasText = "inline.png" });
        var imagePath = await choice.Locator("img").GetAttributeAsync("src");
        Assert.NotNull(imagePath);
        using var publicClient = site.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync(imagePath)).StatusCode);
        var expectedMarkdown = bodyDuringUpload + "\n\n![圖片](" + imagePath + ")\n\n";
        await choice.ClickAsync();
        await Expect(picker).ToHaveCountAsync(0);
        await Expect(markdown).ToHaveValueAsync(expectedMarkdown);
        await Expect(page.Locator("#content-title")).ToHaveValueAsync(title);
        await page.GetByRole(AriaRole.Button, new() { Name = "預覽", Exact = true }).ClickAsync();
        await Expect(page.Locator(".editor-preview")).ToContainTextAsync("本文不可遺失");
        await Expect(page.Locator(".editor-preview img")).ToHaveAttributeAsync("src", imagePath);
        await SaveScreenshotAsync(page, "inline-authoring.png");
        await page.GetByRole(AriaRole.Button, new() { Name = "Markdown", Exact = true }).ClickAsync();
        await Expect(markdown).ToHaveValueAsync(expectedMarkdown);
        await publish.ClickAsync();
        await page.WaitForURLAsync("**/admin/content/*");
        await Expect(page.Locator(".notice-success")).ToContainTextAsync("內容已建立並公開");

        await using (var db = await App.CreateContextAsync())
        {
            var stored = await db.Contents.Include(content => content.Categories).Include(content => content.Tags).SingleAsync();
            Assert.Equal(title, stored.Title);
            Assert.Equal(slug, stored.Slug);
            Assert.Equal(expectedMarkdown, stored.Markdown);
            Assert.Equal(ContentStatus.Published, stored.Status);
            Assert.NotNull(stored.PublishedAt);
            Assert.Equal("編輯器分類", Assert.Single(stored.Categories).Name);
            Assert.Equal("編輯器標籤", Assert.Single(stored.Tags).Name);
            Assert.Equal(1, await db.Categories.CountAsync());
            Assert.Equal(1, await db.Tags.CountAsync());
            var asset = await db.MediaAssets.SingleAsync();
            Assert.Equal("inline.png", asset.OriginalName);
            Assert.Equal("image/png", asset.ContentType);
            Assert.Equal("/media/" + asset.FileName, imagePath);
        }
        await using var visitorContext = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var visitor = await visitorContext.NewPageAsync();
        var response = await VisitAsync(visitor, "/posts/" + slug);
        Assert.Equal(200, response!.Status);
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true })).ToBeVisibleAsync();
        await Expect(visitor.Locator("article .prose")).ToContainTextAsync("本文不可遺失");
        await Expect(visitor.Locator("article .prose")).ToContainTextAsync("上傳期間繼續寫作。");
        await Expect(visitor.GetByRole(AriaRole.Link, new() { Name = "編輯器分類", Exact = true })).ToBeVisibleAsync();
        await Expect(visitor.GetByRole(AriaRole.Link, new() { Name = "# 編輯器標籤", Exact = true })).ToBeVisibleAsync();
        await Expect(visitor.Locator("article img")).ToHaveAttributeAsync("src", imagePath);
        Assert.True(await visitor.Locator("article img").EvaluateAsync<bool>("img => img.complete && img.naturalWidth > 0"));
        Assert.Empty(pageErrors);
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
