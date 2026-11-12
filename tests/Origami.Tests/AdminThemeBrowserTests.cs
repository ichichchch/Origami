namespace Origami.Tests;

/// <summary>後台黑白設計系統的瀏覽器回歸護欄：令牌必須是中性的墨黑／純白（不隨站台品牌色），
/// 深淺兩式側欄皆為深色錨點，3D 品牌立方體可渲染，reduced-motion 下持續動畫全部停用；
/// 截圖僅落盤到臨時目錄供人工核對，不做影像比對。</summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminThemeBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static readonly string ShotDir =
        Path.Combine(Path.GetTempPath(), "origami-admin-visual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    [PostgresFact]
    public async Task Admin_shell_uses_neutral_ink_palette_in_both_schemes()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(NewContent("主題視覺文章", status: ContentStatus.Published));
        // 站台選定非綠的主題（gallery 棕）：前台必須跟隨所選主題，後台仍是黑白，兩者互不滲透。
        var settings = await actor.Settings.GetAsync();
        settings.ThemePreset = "gallery";
        settings.AccentColor = "#8a5a2b";
        await actor.Settings.SaveAsync(settings);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(environment: "Production");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge",
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString(), ViewportSize = new() { Width = 1440, Height = 900 }
        });
        var page = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);

        try
        {
            await LoginAsync(page, password);
            Directory.CreateDirectory(ShotDir);
            await AssertAdminThemeAsync(page, "light");

            // 前台跟隨所選主題：site-shell 的品牌基色與派生填充色都是站台設定的 gallery 棕。
            await page.GotoAsync("/");
            await page.Locator(".site-shell").WaitForAsync();
            var publicTheme = await page.EvaluateAsync<PublicThemeFacts>("() => {" +
                "const shell = document.querySelector('.site-shell'); const styles = getComputedStyle(shell);" +
                "return { base: styles.getPropertyValue('--accent-base').trim()," +
                "accent: styles.getPropertyValue('--accent').trim() }; }");
            Console.WriteLine($"前台主題事實：base={publicTheme.Base} accent={publicTheme.Accent}");
            Assert.Equal("#8a5a2b", publicTheme.Base);
            Assert.Equal("#8a5a2b", publicTheme.Accent);
            await page.ScreenshotAsync(new() { Path = Path.Combine(ShotDir, "public-gallery.png") });

            // 深色模式：以主題 cookie 直接進入（與 theme-toggle 的寫入相同鍵），伺服端即渲染 data-color-scheme=dark。
            await context.AddCookiesAsync([new()
            {
                Name = "origami.theme", Value = "dark", Url = site.Address.ToString()
            }]);
            await page.GotoAsync("/admin/posts");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await AssertAdminThemeAsync(page, "dark");

            // reduced-motion：全域規則停用所有持續動畫，立方體靜止為定角（靜態替代）。
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            var still = await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.querySelector('.brand-cube')).animationName");
            Assert.Equal("none", still);
            Assert.Empty(pageErrors);
        }
        finally
        {
            Console.WriteLine($"視覺截圖目錄：{ShotDir}");
        }
    }

    private static async Task AssertAdminThemeAsync(IPage page, string scheme)
    {
        await page.Locator(".admin-sidebar").WaitForAsync();
        const string script =
            "() => {" +
            "const shell = document.querySelector('.admin-shell');" +
            "const sidebar = document.querySelector('.admin-sidebar');" +
            "const styles = getComputedStyle(shell);" +
            "const sidebarStyles = getComputedStyle(sidebar);" +
            "const active = sidebar.querySelector('.admin-nav a.active');" +
            "const navLink = sidebar.querySelector('.admin-nav a');" +
            "const button = document.querySelector('.admin-main .button');" +
            "return {" +
            "accent: styles.getPropertyValue('--accent').trim()," +
            "ink: styles.getPropertyValue('--ink').trim()," +
            "scheme: shell.dataset.colorScheme," +
            "sidebarBackground: (sidebarStyles.backgroundImage && sidebarStyles.backgroundImage !== 'none') ? sidebarStyles.backgroundImage : sidebarStyles.backgroundColor," +
            "cubeFaces: document.querySelectorAll('.brand-cube i').length," +
            "mainAnimation: getComputedStyle(document.querySelector('.admin-main')).animationName," +
            "activeBackground: active ? getComputedStyle(active).backgroundColor : 'missing'," +
            "navLinkBrightness: (() => { const c = getComputedStyle(sidebar.querySelector('.admin-nav a')).color;" +
            "let m = c.match(/rgba?\\((\\d+), (\\d+), (\\d+)/); if (m) return Math.min(+m[1], +m[2], +m[3]);" +
            "m = c.match(/color\\(srgb ([\\d.]+) ([\\d.]+) ([\\d.]+)/); if (m) return Math.min(+m[1], +m[2], +m[3]) * 255; return -1; })()," +
            "buttonBackground: button ? getComputedStyle(button).backgroundColor : 'missing'," +
            "topbarPosition: getComputedStyle(document.querySelector('.admin-topbar')).position," +
            "brandPerspective: getComputedStyle(document.querySelector('.brand-mark')).perspective};" +
            "}";
        var facts = await page.EvaluateAsync<ThemeFacts>(script);
        Console.WriteLine($"主題事實[{scheme}]：accent={facts.Accent} sidebar={facts.SidebarBackground} " +
            $"active={facts.ActiveBackground} navBrightness={facts.NavLinkBrightness} button={facts.ButtonBackground} " +
            $"perspective={facts.BrandPerspective}");

        // 淺色＝墨黑填充、深色＝純白填充：後台識別不隨站台品牌色（--accent-base）。
        if (scheme == "light")
        {
            Assert.Equal("#18181b", facts.Accent);
            Assert.Contains("linear-gradient", facts.SidebarBackground, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("#fafafa", facts.Accent);
            Assert.Equal("rgb(11, 11, 14)", facts.SidebarBackground);
        }
        Assert.Equal(6, facts.CubeFaces);
        Assert.Equal("admin-enter", facts.MainAnimation);
        // 現代化細節：active 導覽＝白色膠囊、頂欄吸頂、品牌 3D 透視生效、按鈕填色隨中性 accent。
        Assert.Equal("rgb(255, 255, 255)", facts.ActiveBackground);
        Assert.Equal("sticky", facts.TopbarPosition);
        Assert.NotEqual("none", facts.BrandPerspective);
        if (facts.ButtonBackground != "missing")
        {
            var expectedButton = scheme == "light" ? "rgb(24, 24, 27)" : "rgb(250, 250, 250)";
            Assert.Equal(expectedButton, facts.ButtonBackground);
        }
        // 導覽文字須在深色側欄上可讀：最暗通道不低於 140（color-mix 產出 color(srgb) 或 rgb() 皆可解析）。
        Assert.True(facts.NavLinkBrightness >= 140, $"側欄導覽文字過暗：{facts.NavLinkBrightness}");
        // 截圖供人工核對版面：同一目錄區分主題與頁面。
        var shot = Path.Combine(ShotDir, $"posts-{scheme}.png");
        await page.ScreenshotAsync(new() { Path = shot, FullPage = false });
        if (scheme == "light")
        {
            await page.GotoAsync("/admin");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.Locator(".dashboard-metrics, .empty-state").First.WaitForAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(ShotDir, "dashboard-light.png") });
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

    // 前台主題事實：所選主題的品牌基色須送達瀏覽器並由 site-shell 派生填充色。
    private sealed class PublicThemeFacts
    {
        public string Base { get; set; } = "";
        public string Accent { get; set; } = "";
    }

    // Playwright 的求值結果反序列化需要無參建構與可寫屬性，不能用 record 主建構函式。
    private sealed class ThemeFacts
    {
        public string Accent { get; set; } = "";
        public string Ink { get; set; } = "";
        public string Scheme { get; set; } = "";
        public string SidebarBackground { get; set; } = "";
        public int CubeFaces { get; set; }
        public string MainAnimation { get; set; } = "";
        public string ActiveBackground { get; set; } = "";
        public double NavLinkBrightness { get; set; } = -1;
        public string ButtonBackground { get; set; } = "";
        public string TopbarPosition { get; set; } = "";
        public string BrandPerspective { get; set; } = "";
    }
}
