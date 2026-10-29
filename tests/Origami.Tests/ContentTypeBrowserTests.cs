namespace Origami.Tests;

/// <summary>
/// 自訂內容類型的瀏覽器驗證：後台宣告類型、內容掛到類型、公開專區與文章列表分離。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentTypeBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Admin_declares_a_content_type_and_the_public_section_lists_its_content()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
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
        page.PageError += (_, error) => errors.Enqueue(error);

        try
        {
            await page.GotoAsync("/account/login?lang=en");
            await page.Locator("#username").FillAsync("test-admin");
            await page.Locator("#password").FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**/admin/posts");

            // 後台宣告內容類型：表單送出屬於伺服器事件，須等互動電路接手後才會生效。
            await page.GotoAsync("/admin/types");
            await Expect(page.Locator("h1")).ToHaveTextAsync("Content types");
            await page.Locator("#type-name").FillAsync("Book reviews");
            await page.Locator("#type-slug").FillAsync("reviews");
            for (var attempt = 0; ; attempt++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Save type", Exact = true }).ClickAsync();
                try
                {
                    await Expect(page.Locator(".notice-success")).ToHaveTextAsync("The content type was saved.",
                        new() { Timeout = 2000 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 2) { }
            }
            await Expect(page.Locator("tbody tr")).ToContainTextAsync("Book reviews");

            // 宣告自訂欄位（宣告式擴充點），欄位會出現在內容編輯器。
            await page.GetByRole(AriaRole.Button, new() { Name = "Custom taxonomies" }).First.ClickAsync();
            await page.Locator("#field-name").FillAsync("Publisher");
            await page.Locator("#field-key").FillAsync("publisher");
            for (var attempt = 0; ; attempt++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Save field", Exact = true }).ClickAsync();
                try
                {
                    await Expect(page.Locator(".notice-success")).ToHaveTextAsync("The custom field was saved.",
                        new() { Timeout = 2000 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 2) { }
            }
            await Expect(page.Locator("li")).ToContainTextAsync("Publisher");

            // 內容編輯器可選擇自訂類型，儲存後內容掛到該類型。
            await page.GotoAsync("/admin/posts/new");
            await page.Locator("#content-title").FillAsync("Typed from the editor");
            await page.SelectOptionAsync("#content-type", new SelectOptionValue { Label = "Book reviews" });
            await Expect(page.Locator("#content-type")).ToHaveValueAsync(new Regex("^[0-9a-f-]{36}$"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("/admin/content/", StringComparison.Ordinal));
            Assert.Equal("typed-from-the-editor", await CustomContentSlugAsync("Typed from the editor"));

            // 內容掛到該類型後，只出現在自己的公開專區。
            using (var admin = await App.AdministratorAsync())
            {
                var types = await admin.Services.GetRequiredService<ContentTypeService>().ListAsync();
                var type = Assert.Single(types);
                var content = NewContent("A great book", status: ContentStatus.Published);
                content.ContentTypeId = type.Id;
                await admin.Content.SaveAsync(content);
            }
            await page.GotoAsync("/t/reviews");
            await Expect(page.Locator("h1")).ToContainTextAsync("Book reviews");
            await Expect(page.Locator(".post-list")).ToContainTextAsync("A great book");
            var screenshots = Path.Combine(RunningSite.FindSolutionRoot(), ".artifacts", "screenshots");
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, "content-type-section-en.png"), FullPage = true });

            // 同一份內容不出現在文章列表網址。
            var slug = await CustomContentSlugAsync("A great book");
            using var client = site.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/posts/" + Uri.EscapeDataString(slug))).StatusCode);
            Assert.Empty(errors);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Content type browser flow failed; path=" + new Uri(page.Url).AbsolutePath
                + "; errors=" + string.Join(" | ", errors.Take(8)), error);
        }
    }

    /// <summary>取出自訂類型內容的代稱（自訂類型內容不在文章列表中）。</summary>
    private async Task<string> CustomContentSlugAsync(string title)
    {
        await using var db = await App.CreateContextAsync();
        return await db.Contents.AsNoTracking().Where(x => x.ContentTypeId != null && x.Title == title)
            .Select(x => x.Slug).SingleAsync();
    }
}
