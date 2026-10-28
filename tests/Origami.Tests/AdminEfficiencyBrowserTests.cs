namespace Origami.Tests;

/// <summary>後台效率輪的瀏覽器驗證：列表快速編輯、選圖器搜尋與摘要切點。</summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminEfficiencyBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Quick_edit_picker_search_and_excerpt_from_selection_work_in_browser()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        var seeded = await actor.Content.SaveAsync(NewContent("效率瀏覽器文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        // 媒體檔案必須寫進網站主機的儲存目錄，選圖器搜尋才看得到。
        using var mediaStorage = new TemporaryMediaStorage();
        var media = new MediaService(actor.Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(),
            actor.Services.GetRequiredService<AdminAccess>(), mediaStorage.CreateWithRoot(site.StorageRoot),
            TimeProvider.System, NullLogger<MediaService>.Instance);
        await using (var stream = new MemoryStream(Png(600, 400)))
        {
            await media.UploadAsync(stream, "city.png");
        }
        await using (var stream = new MemoryStream(Png(600, 400)))
        {
            await media.UploadAsync(stream, "forest.png");
        }

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

        try
        {
            await LoginAsync(page, password);

            // 快速編輯：預渲染按鈕可能在電路就緒前接受點擊，以預期 UI 出現為準重試。
            await RetryUntilVisibleAsync(page,
                () => page.GetByRole(AriaRole.Button, new() { Name = "快速編輯 效率瀏覽器文章" }).ClickAsync(),
                () => page.Locator("#quick-title").WaitForAsync(new() { Timeout = 2_000 }));
            await page.Locator("#quick-title").FillAsync("效率瀏覽器新標題");
            await page.Locator("#quick-slug").FillAsync("browser-quick-edit");
            await page.GetByRole(AriaRole.Button, new() { Name = "儲存", Exact = true }).ClickAsync();
            await page.Locator(".content-title", new() { HasTextString = "效率瀏覽器新標題" }).WaitForAsync();
            Assert.Contains("browser-quick-edit", await page.ContentAsync(), StringComparison.Ordinal);

            // 摘要切點：選取本文開頭文字填入摘要欄位。
            await page.GotoAsync("/admin/content/" + seeded.Id);
            await page.Locator("#content-markdown").WaitForAsync();
            await page.Locator("#content-markdown").FillAsync("手工摘要開頭，這是本文其餘內容。");
            await page.EvaluateAsync(
                "() => { const el = document.getElementById('content-markdown'); el.focus(); el.setSelectionRange(0, 6); }");
            await RetryUntilVisibleAsync(page,
                () => page.GetByRole(AriaRole.Button, new() { Name = "以本文選取文字填入" }).ClickAsync(),
                async () =>
                {
                    if (await page.Locator("#content-excerpt").InputValueAsync() is not "手工摘要開頭")
                        throw new TimeoutException("摘要尚未填入選取文字。");
                });

            // 選圖器搜尋：關鍵字過濾媒體庫。列表載入是異步的，先等兩張圖都出現。
            await page.GetByRole(AriaRole.Button, new() { Name = "插入圖片" }).ClickAsync();
            await page.Locator("#picker-search").WaitForAsync();
            try
            {
                await page.WaitForFunctionAsync("() => document.querySelectorAll('.picker-grid .media-choice').length === 2");
            }
            catch (PlaywrightException)
            {
                var pickerHtml = await page.Locator(".media-picker").InnerHTMLAsync();
                Assert.Fail($"選圖器列表未出現。picker HTML 前段：{pickerHtml[..Math.Min(800, pickerHtml.Length)]}");
            }
            await page.Locator("#picker-search").FillAsync("city");
            await page.Locator("#picker-search").PressAsync("Tab");
            try
            {
                await page.WaitForFunctionAsync("() => document.querySelectorAll('.picker-grid .media-choice').length === 1");
            }
            catch (PlaywrightException)
            {
                var html = await page.Locator(".media-picker").InnerHTMLAsync();
                Assert.Fail($"搜尋後列表未過濾。picker HTML 前段：{html[..Math.Min(800, html.Length)]}");
            }
            Assert.Contains("city.png", await page.Locator(".picker-grid").InnerHTMLAsync(), StringComparison.Ordinal);

            Assert.Empty(pageErrors);
        }
        finally
        {
            if (pageErrors.Count > 0)
                throw new Exception("頁面發生錯誤：" + string.Join(" | ", pageErrors));
        }
    }

    private static async Task LoginAsync(IPage page, string password)
    {
        await page.GotoAsync("/account/login");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>預渲染互動頁的按鈕可能在電路就緒前被點擊；以預期條件出現與否重試。</summary>
    private static async Task RetryUntilVisibleAsync(IPage page, Func<Task> action, Func<Task> expectation)
    {
        for (var attempt = 0; ; attempt++)
        {
            await action();
            try
            {
                await expectation();
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is PlaywrightException or TimeoutException)
            {
                await page.WaitForTimeoutAsync(400);
            }
        }
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90)!;
        return encoded.ToArray();
    }
}
