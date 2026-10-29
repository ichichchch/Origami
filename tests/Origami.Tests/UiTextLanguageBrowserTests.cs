namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class UiTextLanguageBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Language_selection_survives_navigation_and_interactive_member_edits()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        var member = await CreateMemberAsync(SiteRole.Editor, name: "language-editor");
        using (var admin = await App.AdministratorAsync())
        {
            var post = await admin.Content.SaveAsync(NewContent("Comment smoke", status: ContentStatus.Published));
            await using var db = await App.CreateContextAsync();
            db.Comments.Add(new ContentComment
            {
                Id = Guid.NewGuid(), ContentId = post.Id!.Value, AuthorName = "Smoke visitor",
                Markdown = "A reviewable comment", Status = CommentStatus.Pending, CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        await using var site = new RunningSite(App.ConnectionString);
        using var mediaStorage = new TemporaryMediaStorage();
        using (var admin = await App.AdministratorAsync())
        {
            var media = new MediaService(admin.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
                admin.Services.GetRequiredService<AdminAccess>(), mediaStorage.CreateWithRoot(site.StorageRoot),
                TimeProvider.System, NullLogger<MediaService>.Instance);
            using var bitmap = new SKBitmap(new SKImageInfo(512, 512));
            bitmap.Erase(SKColors.SteelBlue);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            using var stream = new MemoryStream(encoded.ToArray());
            var icon = await media.UploadAsync(stream, "language-smoke.png");
            var settings = await admin.Settings.GetAsync();
            settings.SiteIconMediaId = icon.Id;
            await admin.Settings.SaveAsync(settings);
        }
        await site.StartAsync(environment: "Development");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge", Headless = true
        });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = site.Address.ToString(), IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1366, Height = 900 }
        });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        var assets = new ConcurrentQueue<(string Type, int Status, string Mime)>();
        page.PageError += (_, error) => errors.Enqueue(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Enqueue(message.Text + " at " + message.Location);
        };
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
                errors.Enqueue(response.Status + " " + new Uri(response.Url).AbsolutePath);
            if (response.Request.ResourceType is "script" or "stylesheet")
                assets.Enqueue((response.Request.ResourceType, response.Status,
                    response.Headers.GetValueOrDefault("content-type", "")));
        };
        page.RequestFailed += (_, request) =>
        {
            if (request.ResourceType is "script" or "stylesheet") errors.Enqueue(request.Failure ?? "Asset request failed");
        };

        try
        {
            await page.GotoAsync("/search?page=2&lang=en");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "en");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Semantic search", Exact = true })).ToBeVisibleAsync();
            await page.Locator(".language-switcher a[lang='zh-Hant']").ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("lang=zh-Hant", StringComparison.Ordinal));
            Assert.Contains("page=2", page.Url);
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "zh-Hant");
            await page.Locator(".language-switcher a[lang='en']").ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("lang=en", StringComparison.Ordinal));

            await page.GotoAsync("/account/login");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Administrator sign-in");
            await page.Locator("#username").FillAsync("test-admin");
            await page.Locator("#password").FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/admin/posts");
            await Expect(page.Locator(".admin-nav")).ToContainTextAsync("Site members");
            await page.GotoAsync("/admin/members");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "en");
            var row = page.Locator("tbody tr").Filter(new() { HasText = "language-editor" });
            for (var attempt = 0; ; attempt++)
            {
                await row.GetByRole(AriaRole.Button, new() { Name = "Change role", Exact = true }).ClickAsync();
                try
                {
                    await Expect(page.Locator("#member-name")).ToHaveValueAsync("language-editor", new() { Timeout = 1500 });
                    await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Change member role", Exact = true })).ToBeVisibleAsync();
                    break;
                }
                catch (PlaywrightException) when (attempt < 2) { }
            }
            await page.Locator("#member-display-name").FillAsync("English byline");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save member", Exact = true }).ClickAsync();
            await Expect(page.Locator(".notice-success")).ToHaveTextAsync("Member role updated.");
            await Expect(row).ToContainTextAsync("English byline");
            using (var admin = await App.AdministratorAsync())
            {
                var saved = Assert.Single(await admin.Sites.ListMembersAsync(), value => value.UserId == member.Id);
                Assert.Equal("English byline", saved.DisplayName);
            }
            await page.ReloadAsync();
            await Expect(page.Locator("h1")).ToHaveTextAsync("Site members");
            await Expect(page.Locator("tbody tr").Filter(new() { HasText = "language-editor" })).ToContainTextAsync("English byline");
            var screenshots = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, "i18n-members-en.png"), FullPage = true });
            await page.SetViewportSizeAsync(390, 844);
            await Expect(page.Locator(".language-switcher a[lang='zh-Hant']")).ToBeVisibleAsync();
            await page.Locator(".language-switcher a[lang='zh-Hant']").ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("lang=zh-Hant", StringComparison.Ordinal));
            await Expect(page.Locator("h1")).ToHaveTextAsync("站點成員");
            await page.GotoAsync("/admin/members?lang=unsupported");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "zh-Hant");

            await page.GotoAsync("/admin/comments?lang=en");
            var pending = page.Locator(".section-tabs button").Filter(new() { HasText = "Pending review" });
            for (var attempt = 0; ; attempt++)
            {
                await pending.ClickAsync();
                try
                {
                    await Expect(pending).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 1500 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 2) { }
            }
            var comment = page.Locator("tbody tr").Filter(new() { HasText = "Smoke visitor" });
            await comment.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Approve") }).ClickAsync();
            await Expect(page.Locator(".notice-success")).ToHaveTextAsync("Comment status updated.");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No matching comments", Exact = true })).ToBeVisibleAsync();
            await page.Locator(".section-tabs button").Filter(new() { HasText = "Approved" }).ClickAsync();
            await Expect(comment).ToContainTextAsync("Approved");
            Assert.NotEmpty(assets);
            Assert.All(assets, asset =>
            {
                Assert.InRange(asset.Status, 200, 299);
                Assert.Contains(asset.Type == "script" ? "javascript" : "text/css", asset.Mime);
            });
            Assert.Empty(errors);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Language UI smoke failed; path=" + new Uri(page.Url).AbsolutePath
                + "; errors=" + string.Join(" | ", errors.Take(8)) + "; assetFailures="
                + string.Join(" | ", assets.Where(asset => asset.Status >= 400)), error);
        }
    }
}
