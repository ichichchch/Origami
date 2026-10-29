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
        Assert.Contains("--accent:#123456", html, StringComparison.Ordinal);
        Assert.Contains("--reading-width:72ch", html, StringComparison.Ordinal);
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
        await page.SelectOptionAsync("#theme-preset", "gallery");
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
        Assert.Contains("--accent:#8a5a2b", style, StringComparison.Ordinal);
        Assert.Empty(errors);
    }
}
