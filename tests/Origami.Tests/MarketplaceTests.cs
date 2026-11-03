namespace Origami.Tests;

/// <summary>
/// 受控包市場：manifest 驗證邊界、ZIP 容器安全、安裝／啟停／解除生命週期、
/// 跨站隔離與角色權限，以及主題包對公開設定投影的影響。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MarketplaceTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [Fact]
    public void Manifest_validation_rejects_unsafe_declarations()
    {
        // 字體棧只接受白名單鍵：自由字串是 style 屬性注入的入口，必須在驗證階段擋下。
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(
            ThemeManifest(fontBody: "serif; } body { display:none")));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(
            ThemeManifest(fontHeading: "url(https://evil.test/x)")));

        // 色碼、欄寬、卡片樣式與密度數值皆有白名單或界限。
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(accent: "red")));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(accent: "#12")));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(width: 47)));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(width: 97)));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(cardStyle: "evil")));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(proseLine: 1.0)));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(listGap: 3.5)));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(headingScale: 2.0)));

        // 包 CSS 走自訂 CSS 同一套清理器：危險構造直接拒絕。
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(
            ThemeManifest(css: "@import url('https://evil.test/x.css');")));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(
            ThemeManifest(css: "a { background: url(javascript:alert(1)); }")));

        // 代稱：不得含路徑字元，不得與內建版型預設同名。
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(slug: "../evil")));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(ThemeManifest(slug: "reading")));

        // 小工具包：至少一項、且不超過單包上限。
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(
            new PackageManifest("pack-a", "空包", "描述", PackageKind.Widgets, null, [])));
        Assert.Throws<CmsException>(() => MarketplaceRules.ValidateManifest(
            new PackageManifest("pack-b", "太多包", "描述", PackageKind.Widgets, null,
                [.. Enumerable.Range(0, Limits.PackageMaxWidgets + 1)
                    .Select(i => new ManifestWidget(WidgetArea.Sidebar, WidgetType.Search, $"項目 {i}", null, null, null))])));

        // 合法宣告通過驗證並可標準化序列化。
        var valid = MarketplaceRules.ValidateManifest(ThemeManifest(css: "/* 註解 */ a { color: red }"));
        Assert.Equal("ink", valid.Slug);
        Assert.Equal("a { color: red }", valid.Theme!.Css); // 註解已移除
        var canonical = MarketplaceRules.Canonicalize(valid);
        Assert.Contains("\"slug\":\"ink\"", canonical);
    }

    [PostgresFact]
    public async Task Zip_upload_rejects_malformed_containers_and_unsafe_manifests()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();
        var manifest = MarketplaceRules.Canonicalize(MarketplaceRules.ValidateManifest(ThemeManifest()));

        // 非 ZIP（偽裝成 JSON）一律拒絕。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeZipAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(manifest))));

        // 缺少根目錄 manifest.json。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeZipAsync(
            new MemoryStream(PackageZip("{}", "other.json"))));

        // 路徑穿越名稱不是根目錄 manifest.json。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeZipAsync(
            new MemoryStream(PackageZip(manifest, "../manifest.json"))));

        // 夾帶其他條目一律整包拒絕（包不攜帶二進位資源）。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeZipAsync(
            new MemoryStream(PackageZip(manifest, "manifest.json", includeExtra: true))));

        // manifest 內容不安全同樣拒絕（字體棧自由字串）。
        var evil = MarketplaceRules.Canonicalize(new PackageManifest("evil", "危險包", "描述", PackageKind.Theme,
            new ThemeManifest("#1f3a5f", 66, true, true, "list", "serif; } body { display:none", "serif",
                2.1, 2.4, 1.05, null), null));
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeZipAsync(
            new MemoryStream(PackageZip(evil))));

        // 合法包可解析預覽，且不落地任何資料。
        var preview = await marketplace.AnalyzeZipAsync(new MemoryStream(PackageZip(manifest)));
        Assert.Equal("ink", preview.Slug);
        Assert.Equal("墨韻", preview.Name);
        Assert.Empty(await marketplace.InstalledAsync());
    }

    [PostgresFact]
    public async Task Theme_pack_lifecycle_applies_and_restores_the_site_layout()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();

        var installed = await marketplace.InstallFromCatalogAsync("ink");
        Assert.Equal(PackageKind.Theme, installed.Kind);
        Assert.False(installed.IsEnabled);

        // 重複安裝拒絕。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.InstallFromCatalogAsync("ink"));

        // 啟用即套用版型：設定的版型預設改為 pkg: 前綴值，純量欄位複製自包宣告。
        var enabled = await marketplace.SetEnabledAsync(installed.Id, true, installed.Version);
        Assert.True(enabled.IsEnabled);
        var settings = await admin.Settings.GetAsync();
        Assert.Equal("pkg:ink", settings.ThemePreset);
        Assert.Equal("#1f3a5f", settings.AccentColor);
        Assert.Equal(66, settings.ReadingWidth);

        // 公開投影帶出白名單字體棧（內建常數），不是包內的自由字串。
        var active = await marketplace.ActiveThemeAsync();
        Assert.NotNull(active);
        Assert.Equal("ink", active!.Slug);
        Assert.Contains("Noto Serif TC", active.FontBody, StringComparison.Ordinal);

        // 停用退回內建閱讀預設。
        var disabled = await marketplace.SetEnabledAsync(enabled.Id, false, enabled.Version);
        Assert.False(disabled.IsEnabled);
        Assert.Equal("reading", (await admin.Settings.GetAsync()).ThemePreset);
        Assert.Null(await marketplace.ActiveThemeAsync());

        // 啟用狀態下解除安裝：站台版型同樣退回閱讀預設。
        var again = await marketplace.SetEnabledAsync(enabled.Id, true, disabled.Version);
        await marketplace.UninstallAsync(again.Id, again.Version);
        Assert.Equal("reading", (await admin.Settings.GetAsync()).ThemePreset);
        Assert.Empty(await marketplace.InstalledAsync());
    }

    [PostgresFact]
    public async Task Widget_pack_install_materializes_widgets_and_uninstall_removes_them()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();
        var widgets = admin.Services.GetRequiredService<WidgetService>();

        // 管理者自行建立的小工具不屬於任何包，解除安裝時必須保留。
        var manual = await widgets.SaveAsync(new WidgetService.WidgetInput
            { Area = WidgetArea.Footer, Type = WidgetType.Search });

        var installed = await marketplace.InstallFromCatalogAsync("sidebar-starter");
        Assert.Equal(PackageKind.Widgets, installed.Kind);
        Assert.Equal(3, installed.WidgetCount);

        // 安裝即物化為站點小工具，並帶上包來源標記。
        var listed = await widgets.ListAsync();
        Assert.Equal(4, listed.Count);
        Assert.Equal(3, listed.Count(x => x.PackageSlug == "sidebar-starter"));

        // 公開投影包含包小工具。
        Assert.Equal(4, (await widgets.PublicAsync()).Count);

        // 停用只影響公開投影，不動已建立的小工具資料列。
        var disabled = await marketplace.SetEnabledAsync(installed.Id, false, installed.Version);
        Assert.Equal(3, (await widgets.ListAsync()).Count(x => x.PackageSlug == "sidebar-starter"));
        Assert.Single(await widgets.PublicAsync());

        // 解除安裝連同刪除包建立的小工具，管理者自建者保留。
        await marketplace.UninstallAsync(disabled.Id, disabled.Version);
        var remaining = await widgets.ListAsync();
        Assert.Single(remaining);
        Assert.Equal(manual.Id, remaining[0].Id);
    }

    [PostgresFact]
    public async Task Packages_require_administrator_and_are_isolated_per_site()
    {
        using (var anonymous = App.Anonymous())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                anonymous.Services.GetRequiredService<MarketplaceService>().InstalledAsync());
        }

        // 編輯者可瀏覽目錄，但不能安裝。
        await CreateMemberAsync(SiteRole.Editor, null, "editor");
        using var editor = await App.UserAsync(await AdminUserIdFor("editor"));
        var editorMarketplace = editor.Services.GetRequiredService<MarketplaceService>();
        Assert.NotEmpty(await editorMarketplace.CatalogAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => editorMarketplace.InstallFromCatalogAsync("ink"));

        // 站點隔離：A 站安裝的主題包不影響 B 站。
        using var admin = await App.AdministratorAsync();
        await admin.Services.GetRequiredService<MarketplaceService>().InstallFromCatalogAsync("ink");

        var site = await CreateSiteAsync("marketsecond");
        using var otherAdmin = await App.AdministratorAsync(site.Id);
        var otherMarketplace = otherAdmin.Services.GetRequiredService<MarketplaceService>();
        Assert.Empty(await otherMarketplace.InstalledAsync());
        Assert.Null(await otherMarketplace.ActiveThemeAsync());
        Assert.All(await otherMarketplace.CatalogAsync(), x => Assert.False(x.IsInstalled));
    }

    [PostgresFact]
    public async Task Settings_accept_installed_package_preset_and_mirror_the_enabled_state()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();
        var installed = await marketplace.InstallFromCatalogAsync("sunlit");

        // 設定頁選擇已安裝的主題包：儲存成功並同步啟用鏡像。
        // 使用者微調值（強調色、欄寬等）由設定頁自行保留；包的字體與密度另由公開投影帶出。
        var settings = await admin.Settings.GetAsync();
        settings.ThemePreset = "pkg:sunlit";
        var saved = await admin.Settings.SaveAsync(settings);
        Assert.Equal("pkg:sunlit", saved.ThemePreset);
        Assert.Equal(settings.AccentColor, saved.AccentColor);
        var packages = await marketplace.InstalledAsync();
        var package = Assert.Single(packages, x => x.Id == installed.Id);
        Assert.True(package.IsEnabled);

        // 啟用主題包時，包的強調色與欄寬會套用進站台設定（市場頁路徑）。
        await marketplace.SetEnabledAsync(installed.Id, true, package.Version);
        var applied = await admin.Settings.GetAsync();
        Assert.Equal("#b3541e", applied.AccentColor);
        Assert.Equal(70, applied.ReadingWidth);

        // 未安裝的主題包不得被寫入設定（避免懸空引用）。
        settings = await admin.Settings.GetAsync();
        settings.ThemePreset = "pkg:missing";
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));
    }

    [PostgresFact]
    public async Task Export_import_round_trips_packages_and_their_widgets()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();
        var widgets = admin.Services.GetRequiredService<WidgetService>();
        await marketplace.InstallFromCatalogAsync("sidebar-starter");
        var installed = await marketplace.InstallFromCatalogAsync("ink");
        await marketplace.SetEnabledAsync(installed.Id, true, installed.Version);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        // 包與其小工具隨匯出檔往返；主題包的啟用狀態一併還原。
        var restored = await marketplace.InstalledAsync();
        Assert.Equal(2, restored.Count);
        Assert.True(Assert.Single(restored, x => x.Kind == PackageKind.Theme).IsEnabled);
        var listed = await widgets.ListAsync();
        Assert.Equal(3, listed.Count(x => x.PackageSlug == "sidebar-starter"));
        Assert.Equal("pkg:ink", (await admin.Settings.GetAsync()).ThemePreset);
    }

    [PostgresFact]
    public async Task Admin_installs_and_enables_a_theme_pack_and_the_public_site_follows()
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

        // 市場頁：瀏覽內建目錄 → 安裝主題包 → 啟用。
        await page.GotoAsync("/admin/marketplace");
        var card = page.Locator(".package-card[data-package-slug='ink']");
        await Expect(card).ToBeVisibleAsync();
        await card.GetByRole(AriaRole.Button, new() { Name = "Install", Exact = true }).ClickAsync();

        var row = page.Locator("table tbody tr[data-package-slug='ink']");
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.Locator("button[data-package-action='enable']").ClickAsync();

        // 啟用後按鈕切換為停用：代表伺服器已套用主題包。
        await Expect(row.Locator("button[data-package-action='disable']"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // 站台設定確實切換到主題包：版型預設改為 pkg: 前綴值，強調色與欄寬取自包宣告。
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        Assert.Equal("pkg:ink", settings.ThemePreset);
        Assert.Equal("#1f3a5f", settings.AccentColor);

        // 公開網站改用主題包的強調色、字體與密度變數。
        await page.GotoAsync("/");
        var shell = page.Locator(".site-shell");
        var style = await shell.GetAttributeAsync("style");
        Assert.NotNull(style);
        Assert.Contains("--accent-base:#1f3a5f", style, StringComparison.Ordinal);
        Assert.Contains("--prose-line:2.1", style, StringComparison.Ordinal);
        Assert.Contains("--list-gap:2.4rem", style, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    [PostgresFact]
    public async Task WordPress_theme_is_converted_to_a_controlled_pack_without_keeping_code()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();

        // 分析：只讀 style.css 標頭的中繼資料與可推導的視覺參數。
        var preview = await marketplace.AnalyzeWordPressThemeAsync(new MemoryStream(WordPressThemeZip()));
        Assert.Equal("My Theme", preview.Name);
        Assert.Equal("wp-my-theme", preview.Slug);
        Assert.NotNull(preview.Theme);
        Assert.Equal("#1f3a5f", preview.Theme!.AccentColor); // 樣式中最常見的非灰階色碼
        Assert.Equal("serif", preview.Theme.FontBody); // font-family 為衬线家族
        Assert.Empty(preview.Widgets); // 主題包不攜帶小工具定義

        // 安装：成为受控主题包，模板与程序代码不落地、不执行。
        var installed = await marketplace.InstallWordPressThemeAsync(new MemoryStream(WordPressThemeZip()));
        Assert.Equal(PackageKind.Theme, installed.Kind);
        Assert.False(installed.IsEnabled); // 主题包须另行启用
        await using var db = await App.CreateContextAsync();
        var row = await db.SitePackages.AsNoTracking().SingleAsync(x => x.Slug == "wp-my-theme");
        Assert.DoesNotContain("<?php", row.ManifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("get_header", row.ManifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.Null(MarketplaceRules.Validate(row.ManifestJson).Theme!.Css); // 第三方样式不直接带入
    }

    [PostgresFact]
    public async Task WordPress_theme_import_rejects_archives_without_usable_style_header()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();

        // 缺少 style.css。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeWordPressThemeAsync(
            new MemoryStream(WordPressThemeZip(styleCss: null, includeStyle: false))));

        // style.css 缺少 Theme Name 标头。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeWordPressThemeAsync(
            new MemoryStream(WordPressThemeZip(styleCss: "body { color: #123456; }"))));

        // 非 ZIP 封装。
        await Assert.ThrowsAsync<CmsException>(() => marketplace.AnalyzeWordPressThemeAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));
    }

    [PostgresFact]
    public async Task Package_export_round_trips_through_the_marketplace()
    {
        using var admin = await App.AdministratorAsync();
        var marketplace = admin.Services.GetRequiredService<MarketplaceService>();
        var installed = await marketplace.InstallFromCatalogAsync("sunlit");

        // 汇出为可分享的 ZIP：只含 manifest.json。
        var bytes = await marketplace.ExportAsync(installed.Id, installed.Version);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal("manifest.json", Assert.Single(archive.Entries).FullName);

        // 先解除原包，再以汇出的 ZIP 重新安装：内容一致。
        await marketplace.UninstallAsync(installed.Id, installed.Version);
        var restored = await marketplace.InstallFromZipAsync(new MemoryStream(bytes));
        Assert.Equal("sunlit", restored.Slug);
        Assert.Equal(PackageKind.Theme, restored.Kind);
        Assert.Equal("#b3541e", restored.Theme!.AccentColor);
    }

    private async Task<string> AdminUserIdFor(string userName)
    {
        using var admin = await App.AdministratorAsync();
        var user = await admin.Users.FindByNameAsync(userName);
        return user!.Id;
    }

    private static PackageManifest ThemeManifest(string slug = "ink", string accent = "#1f3a5f", int width = 66,
        string cardStyle = "list", string fontBody = "serif", string fontHeading = "serif",
        double proseLine = 2.1, double listGap = 2.4, double headingScale = 1.05, string? css = null) =>
        new(slug, "墨韻", "測試主題包", PackageKind.Theme,
            new ThemeManifest(accent, width, true, true, cardStyle, fontBody, fontHeading,
                proseLine, listGap, headingScale, css), null);

    /// <summary>建立測試用包 ZIP：預設只有根目錄 manifest.json，可指定條目名稱或夾帶額外條目。</summary>
    private static byte[] PackageZip(string manifestJson, string entryName = "manifest.json", bool includeExtra = false)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open()))
                writer.Write(manifestJson);
            if (includeExtra)
            {
                using var extra = new StreamWriter(archive.CreateEntry("extra.txt").Open());
                extra.Write("夾帶檔案");
            }
        }
        return buffer.ToArray();
    }

    /// <summary>建立測試用 WordPress 主題封裝：含 style.css 標頭與模板檔（模板僅供測試，
    /// 驗證匯入流程不會保留或執行它們）。</summary>
    private static byte[] WordPressThemeZip(string? styleCss = null, bool includeStyle = true)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeStyle)
            {
                using (var writer = new StreamWriter(archive.CreateEntry("mytheme/style.css").Open()))
                    writer.Write(styleCss ?? """
                        /*
                        Theme Name: My Theme
                        Theme URI: https://example.test/theme
                        Author: Someone
                        Description: A sample theme for conversion.
                        Version: 1.2.3
                        License: GNU General Public License v2 or later
                        License URI: https://www.gnu.org/licenses/gpl-2.0.html
                        Text Domain: mytheme
                        Tags: blog, two-columns
                        */
                        body { color: #1f3a5f; background: #ffffff; }
                        h1 { color: #1f3a5f; }
                        a { color: #1f3a5f; }
                        p { font-family: Georgia, "Times New Roman", serif; }
                        """);
            }
            using (var template = new StreamWriter(archive.CreateEntry("mytheme/index.php").Open()))
                template.Write("<?php get_header(); ?>");
            using (var functions = new StreamWriter(archive.CreateEntry("mytheme/functions.php").Open()))
                functions.Write("<?php add_action('init', function () {}); ?>");
        }
        return buffer.ToArray();
    }
}
