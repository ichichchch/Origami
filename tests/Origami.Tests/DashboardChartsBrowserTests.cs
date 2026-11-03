namespace Origami.Tests;

/// <summary>儀表板圖表化：Chart.js 初始化、fallback 切換、點擊導向與無瀏覽降級。</summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardChartsBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Dashboard_charts_render_replace_fallback_and_navigate_in_browser()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        var topPost = await actor.Content.SaveAsync(NewContent("圖表熱門文章", status: ContentStatus.Published));
        var quietPost = await actor.Content.SaveAsync(NewContent("圖表長尾文章", status: ContentStatus.Published));
        for (var i = 0; i < 3; i++) await actor.Content.RecordViewAsync(topPost.Id!.Value);
        await actor.Content.RecordViewAsync(quietPost.Id!.Value);

        await using var site = new RunningSite(App.ConnectionString);
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
            await page.GotoAsync("/admin");

            // OnChartsReady 回呼後 Blazor 還要重繪一次才隱藏 fallback；與 JS 端圖表就緒有競態，併入同一等待。
            await page.WaitForFunctionAsync(
                "() => window.Chart && window.Chart.getChart(document.getElementById('trend-chart')) && window.Chart.getChart(document.getElementById('top-chart'))"
                + " && document.querySelector('.trend-bars').hidden && document.querySelector('.table-scroll').hidden");

            // 主題三態切換後圖表配色需即時更新（--accent 在深色模式為 color-mix 運算式）。
            var lightAccent = await page.EvaluateAsync<string>(
                "() => window.Chart.getChart(document.getElementById('trend-chart')).data.datasets[0].borderColor");
            await page.Locator("[data-theme-toggle]").ClickAsync();
            await page.Locator("[data-theme-toggle]").ClickAsync();
            await page.WaitForFunctionAsync(
                "() => window.Chart.getChart(document.getElementById('trend-chart')).data.datasets[0].borderColor !== '" + lightAccent + "'");

            // 點擊熱門條形圖第一列（依 30 日瀏覽排序的第一篇），應導向該內容的編輯頁。
            var center = await page.EvaluateAsync<CanvasPoint>(
                "() => { const p = window.Chart.getChart(document.getElementById('top-chart')).getDatasetMeta(0).data[0].getCenterPoint(); return { X: p.x, Y: p.y }; }");
            await page.Locator("#top-chart").ClickAsync(new() { Position = new() { X = (float)center.X, Y = (float)center.Y } });
            await page.WaitForURLAsync($"**/admin/content/{topPost.Id}");

            Assert.Empty(pageErrors);
        }
        finally
        {
            if (pageErrors.Count > 0)
                throw new Exception("頁面發生錯誤：" + string.Join(" | ", pageErrors));
        }
    }

    [PostgresFact]
    public async Task Dashboard_without_views_keeps_summary_text_and_skips_charts()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        await actor.Content.SaveAsync(NewContent("無瀏覽紀錄文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
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
            await page.GotoAsync("/admin");

            await page.GetByText("近期尚無公開瀏覽紀錄。").WaitForAsync();
            await page.GetByText("尚無熱門內容。").WaitForAsync();
            Assert.Equal(0, await page.Locator("canvas").CountAsync());

            Assert.Empty(pageErrors);
        }
        finally
        {
            if (pageErrors.Count > 0)
                throw new Exception("頁面發生錯誤：" + string.Join(" | ", pageErrors));
        }
    }

    // Playwright 的 evaluate 反序列化走 Activator，需要無參數建構子與可寫屬性。
    private sealed class CanvasPoint { public double X { get; set; } public double Y { get; set; } }

    private static async Task LoginAsync(IPage page, string password)
    {
        await page.GotoAsync("/account/login");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
