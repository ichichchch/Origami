namespace Origami.Tests;

/// <summary>多站點批次的管理頁瀏覽器驗證：使用者、成員、站點與 API 憑證。</summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminTenancyBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Users_members_sites_and_credentials_pages_manage_tenancy_in_browser()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);

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
            const string userName = "browser-user";
            const string userPassword = "Browser-User-2026!A";

            // 使用者：建立 → 停用 → 重新啟用。
            await page.GotoAsync("/admin/users");
            await RetryAsync(page,
                () => page.Locator("#new-user-name").FillAsync(userName),
                () => page.Locator("#new-user-name").WaitForAsync(new() { Timeout = 2_000 }));
            await page.Locator("#new-user-name").FillAsync(userName);
            await page.Locator("#new-user-password").FillAsync(userPassword);
            await RetryAsync(page,
                () => page.GetByRole(AriaRole.Button, new() { Name = "建立使用者" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex(Regex.Escape(userName)) }).WaitForAsync(new() { Timeout = 2_000 }));
            var userRow = page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex(Regex.Escape(userName)) });
            await RetryAsync(page,
                () => userRow.GetByRole(AriaRole.Button, new() { Name = "停用帳號" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Button, new() { Name = "確認變更" }).WaitForAsync(new() { Timeout = 2_000 }));
            await page.GetByRole(AriaRole.Button, new() { Name = "確認變更" }).ClickAsync();
            await userRow.GetByRole(AriaRole.Button, new() { Name = "重新啟用" }).WaitForAsync();
            await RetryAsync(page,
                () => userRow.GetByRole(AriaRole.Button, new() { Name = "重新啟用" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Button, new() { Name = "確認變更" }).WaitForAsync(new() { Timeout = 2_000 }));
            await page.GetByRole(AriaRole.Button, new() { Name = "確認變更" }).ClickAsync();
            await userRow.GetByRole(AriaRole.Button, new() { Name = "停用帳號" }).WaitForAsync();

            // 成員：加入既有帳號為編輯者，再移除。
            await page.GotoAsync("/admin/members");
            await RetryAsync(page,
                () => page.Locator("#member-name").FillAsync(userName),
                () => page.Locator("#member-name").WaitForAsync(new() { Timeout = 2_000 }));
            await page.Locator("#member-name").FillAsync(userName);
            await page.Locator("#member-role").SelectOptionAsync("Editor");
            await RetryAsync(page,
                () => page.GetByRole(AriaRole.Button, new() { Name = "儲存成員" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex(Regex.Escape(userName)) })
                    .GetByText("編輯者").WaitForAsync(new() { Timeout = 2_000 }));
            var memberRow = page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex(Regex.Escape(userName)) });
            await RetryAsync(page,
                () => memberRow.GetByRole(AriaRole.Button, new() { Name = "移除" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Button, new() { Name = "確認移除" }).WaitForAsync(new() { Timeout = 2_000 }));
            await page.GetByRole(AriaRole.Button, new() { Name = "確認移除" }).ClickAsync();
            await memberRow.WaitForAsync(new() { State = WaitForSelectorState.Detached });

            // 站點：建立新站，再以編輯表單改名。
            await page.GotoAsync("/admin/sites");
            await RetryAsync(page,
                () => page.Locator("#new-site-name").FillAsync("第四站"),
                () => page.Locator("#new-site-name").WaitForAsync(new() { Timeout = 2_000 }));
            await page.Locator("#new-site-name").FillAsync("第四站");
            await page.Locator("#new-site-slug").FillAsync("fourth");
            await page.Locator("#new-site-host").FillAsync("fourth.example.test");
            await RetryAsync(page,
                () => page.GetByRole(AriaRole.Button, new() { Name = "建立站點" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex("第四站") }).WaitForAsync(new() { Timeout = 3_000 }));
            var siteRow = page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex("第四站") });
            await siteRow.GetByRole(AriaRole.Button, new() { Name = "編輯" }).ClickAsync();
            await page.Locator("#edit-site-name").FillAsync("第四站改名");
            await page.GetByRole(AriaRole.Button, new() { Name = "儲存站點" }).ClickAsync();
            await page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex("第四站改名") }).WaitForAsync();

            // 憑證：建立（秘密只顯示一次）→ 隱藏 → 撤銷。
            await page.GotoAsync("/admin/credentials");
            await RetryAsync(page,
                () => page.Locator("#credential-name").FillAsync("瀏覽器憑證"),
                () => page.Locator("#credential-name").WaitForAsync(new() { Timeout = 2_000 }));
            await page.Locator("#credential-name").FillAsync("瀏覽器憑證");
            await page.Locator("#credential-days").FillAsync("30");
            await RetryAsync(page,
                () => page.GetByRole(AriaRole.Button, new() { Name = "建立憑證" }).ClickAsync(),
                () => page.Locator("#credential-secret").WaitForAsync(new() { Timeout = 3_000 }));
            var secret = await page.Locator("#credential-secret").InputValueAsync();
            Assert.Matches("\\Aorg_[0-9a-f]{32}\\.[0-9a-f]{64}\\z", secret);
            await page.GetByRole(AriaRole.Button, new() { Name = "隱藏並清除" }).ClickAsync();
            await page.Locator("#credential-secret").WaitForAsync(new() { State = WaitForSelectorState.Detached });
            // 隱藏後秘密不再出現在頁面任何地方。
            Assert.DoesNotContain(secret[37..], await page.ContentAsync(), StringComparison.Ordinal);
            var credentialRow = page.GetByRole(AriaRole.Row, new() { NameRegex = new Regex("瀏覽器憑證") });
            await RetryAsync(page,
                () => credentialRow.GetByRole(AriaRole.Button, new() { Name = "撤銷" }).ClickAsync(),
                () => page.GetByRole(AriaRole.Button, new() { Name = "確認撤銷" }).WaitForAsync(new() { Timeout = 2_000 }));
            await page.GetByRole(AriaRole.Button, new() { Name = "確認撤銷" }).ClickAsync();
            await credentialRow.GetByText("已撤銷").WaitForAsync();

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
    private static async Task RetryAsync(IPage page, Func<Task> action, Func<Task> expectation)
    {
        for (var attempt = 0; ; attempt++)
        {
            await action();
            try
            {
                await expectation();
                return;
            }
            catch (PlaywrightException)
            {
                if (attempt >= 20) throw;
            }
            await page.WaitForTimeoutAsync(300);
        }
    }
}
