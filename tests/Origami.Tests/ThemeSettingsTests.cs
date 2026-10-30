namespace Origami.Tests;

/// <summary>
/// 受控版型：預設與版型變數的驗證、公開設定的傳遞，以及前台樣式變數的實際輸出。
/// 版型僅能選用內建預設，不載入任何程式碼或外部樣式。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ThemeSettingsTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Theme_settings_are_validated_and_round_trip_through_the_service()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        Assert.Equal("reading", settings.ThemePreset);
        Assert.Equal("#2f6f4e", settings.AccentColor);
        Assert.Equal(68, settings.ReadingWidth);
        Assert.True(settings.ShowByline);
        Assert.True(settings.ShowCover);
        Assert.Equal(ThemePresets.CardStyleList, settings.CardStyle);

        settings.ThemePreset = "gallery";
        settings.AccentColor = "#8A5A2B";
        settings.ReadingWidth = 72;
        settings.ShowByline = false;
        settings.ShowCover = false;
        settings.CardStyle = ThemePresets.CardStyleGrid;
        await admin.Settings.SaveAsync(settings);

        var saved = await admin.Settings.GetAsync();
        Assert.Equal("gallery", saved.ThemePreset);
        Assert.Equal("#8a5a2b", saved.AccentColor);
        Assert.Equal(72, saved.ReadingWidth);
        Assert.False(saved.ShowByline);
        Assert.False(saved.ShowCover);
        Assert.Equal(ThemePresets.CardStyleGrid, saved.CardStyle);
    }

    [PostgresFact]
    public async Task Invalid_theme_values_are_refused()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();

        var unknownPreset = await admin.Settings.GetAsync();
        unknownPreset.ThemePreset = "not-a-preset";
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(unknownPreset));

        var badColor = await admin.Settings.GetAsync();
        badColor.AccentColor = "red; background: url(x)";
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(badColor));

        var badWidth = await admin.Settings.GetAsync();
        badWidth.ReadingWidth = 4;
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(badWidth));

        var badCard = await admin.Settings.GetAsync();
        badCard.CardStyle = "carousel";
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(badCard));

        // 失敗的儲存不得改動既有設定。
        var current = await admin.Settings.GetAsync();
        Assert.Equal(settings.ThemePreset, current.ThemePreset);
        Assert.Equal(settings.AccentColor, current.AccentColor);
    }

    [PostgresFact]
    public async Task Public_settings_expose_the_theme_to_the_frontend()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.AccentColor = "#123456";
        settings.CardStyle = ThemePresets.CardStyleGrid;
        settings.ShowByline = false;
        await admin.Settings.SaveAsync(settings);

        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var client = api.CreateClient();
        using var response = await client.GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"accentColor\":\"#123456\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cardStyle\":\"grid\"", json, StringComparison.Ordinal);
        Assert.Contains("\"showByline\":false", json, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Public_pages_render_the_theme_variables_and_hide_disabled_parts()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.AccentColor = "#123456";
        settings.ReadingWidth = 72;
        settings.ShowByline = false;
        settings.CardStyle = ThemePresets.CardStyleGrid;
        await admin.Settings.SaveAsync(settings);
        await admin.Content.SaveAsync(NewContent("版型文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // 版型變數以 CSS 自訂屬性輸出，並以 data 屬性控制署名與卡片樣式。
        // 注入的是品牌基色 --accent-base；明暗兩式各自在其上派生填充與前景色。
        Assert.Contains("--accent-base:#123456", html, StringComparison.Ordinal);
        // 預設版面變數（reading 襯線、寬鬆行距）與欄寬一併輸出，數值來自內建預設常數。
        Assert.Contains("--reading-width:72ch", html, StringComparison.Ordinal);
        Assert.Contains("--font-body:", html, StringComparison.Ordinal);
        Assert.Contains("--prose-line:2;", html, StringComparison.Ordinal);
        Assert.Contains("--list-gap:2.2rem;", html, StringComparison.Ordinal);
        Assert.Contains("data-card-style=\"grid\"", html, StringComparison.Ordinal);
        Assert.Contains("data-show-byline=\"false\"", html, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Admin_switches_the_theme_preset_and_the_public_site_follows()
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

        await page.GotoAsync("/account/login?lang=en");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");

        await page.GotoAsync("/admin/settings");

        // 即時預覽：容器跟隨站點設定渲染，切換預設後無須儲存立即套用新強調色。
        var preview = page.Locator(".theme-preview");
        await Expect(preview).ToBeVisibleAsync();
        await Expect(preview).ToHaveAttributeAsync("data-color-scheme", "auto");

        await page.SelectOptionAsync("#theme-preset", "gallery");
        await Expect(preview).ToHaveAttributeAsync("data-card-style", "grid");
        var previewStyle = await preview.GetAttributeAsync("style");
        Assert.NotNull(previewStyle);
        Assert.Contains("--accent-base:#8a5a2b", previewStyle, StringComparison.Ordinal);

        for (var attempt = 0; ; attempt++)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Save settings", Exact = true }).ClickAsync();
            try
            {
                await Expect(page.Locator(".notice-success")).ToHaveTextAsync(
                    "Site settings saved. The public site has been updated.", new() { Timeout = 2000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 2) { }
        }

        // 公開網站改用新預設的強調色與卡片樣式。
        await page.GotoAsync("/");
        var shell = page.Locator(".site-shell");
        await Expect(shell).ToHaveAttributeAsync("data-card-style", "grid");
        var style = await shell.GetAttributeAsync("style");
        Assert.NotNull(style);
        Assert.Contains("--accent-base:#8a5a2b", style, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    [PostgresFact]
    public async Task Admin_theme_toggle_cycles_schemes_and_persists_via_cookie()
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

        await page.GotoAsync("/account/login?lang=en");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");

        // 初始為 auto；點擊循環至 light 並寫入 cookie，重載後由服務端讀 cookie 渲染。
        var adminShell = page.Locator(".admin-shell");
        await Expect(adminShell).ToHaveAttributeAsync("data-color-scheme", "auto");
        await page.Locator("[data-theme-toggle]").ClickAsync();
        await Expect(adminShell).ToHaveAttributeAsync("data-color-scheme", "light");
        var scheme = await page.EvaluateAsync<string>("() => (document.cookie.match(/origami\\.theme=(\\w+)/) || [])[1]");
        Assert.Equal("light", scheme);
        await page.ReloadAsync();
        await Expect(page.Locator(".admin-shell")).ToHaveAttributeAsync("data-color-scheme", "light");

        // 公開站台設為 auto 時，訪客明確選擇的 cookie 覆蓋系統探測。
        var publicPage = await context.NewPageAsync();
        await publicPage.GotoAsync("/");
        await Expect(publicPage.Locator(".site-shell")).ToHaveAttributeAsync("data-color-scheme", "light");
        Assert.Empty(errors);
    }

    [PostgresFact]
    public async Task Settings_save_keeps_color_scheme_and_other_unedited_fields()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var admin = await App.AdministratorAsync();
        var before = await admin.Settings.GetAsync();
        before.ColorScheme = ThemePresets.ColorSchemeDark;
        before.MaintenanceMessage = "保存不應清空未編輯的欄位";
        await admin.Settings.SaveAsync(before);

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
        await page.GotoAsync("/account/login?lang=en");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");

        // 只改網站名稱後儲存；色彩模式與維護訊息等未編輯欄位必須原樣保留。
        await page.GotoAsync("/admin/settings");
        await page.Locator("#site-name").FillAsync("不可清空他人欄位");
        for (var attempt = 0; ; attempt++)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Save settings", Exact = true }).ClickAsync();
            try
            {
                await Expect(page.Locator(".notice-success")).ToBeVisibleAsync(new() { Timeout = 2000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 2) { }
        }

        var after = await admin.Settings.GetAsync();
        Assert.Equal(ThemePresets.ColorSchemeDark, after.ColorScheme);
        Assert.Equal("保存不應清空未編輯的欄位", after.MaintenanceMessage);
    }
}
