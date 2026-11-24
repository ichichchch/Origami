namespace Origami.Tests;

/// <summary>搜尋的分類／月份篩選與關鍵詞反白、404 文案與站點預設介面語言等新設定。</summary>
[Collection(PostgresCollection.Name)]
public sealed class SearchSettingsExpansionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Search_filters_highlight_and_new_settings_work()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var admin = await App.AdministratorAsync();
        var search = admin.Services.GetRequiredService<PublicSearchService>();
        var category = await admin.Taxonomy.SaveAsync(TaxonomyKind.Category, new TaxonomyEdit { Name = "旅行" });
        var travel = NewContent("旅行手記", status: ContentStatus.Published);
        travel.CategoryIds = [category.Id!.Value];
        travel.Markdown = "A < B 的咖啡手記，以及更多內容。";
        await admin.Content.SaveAsync(travel);
        await admin.Content.SaveAsync(NewContent("程式設計與咖啡", status: ContentStatus.Published));

        // 關鍵字命中會在摘要中以 mark 反白；內容文字一律 HTML 編碼。
        var all = await search.SearchAsync("咖啡");
        Assert.Equal(2, all.Total);
        var travelHit = all.Hits.Single(x => x.Title == "旅行手記");
        Assert.Contains("<mark>咖啡</mark>", travelHit.SnippetHtml, StringComparison.Ordinal);
        Assert.Contains("A &lt; B", travelHit.SnippetHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", string.Join(" ", all.Hits.Select(x => x.SnippetHtml)), StringComparison.Ordinal);

        // 分類與月份篩選：只做關鍵字比對。
        var filtered = await search.SearchAsync("咖啡", category: category.Slug);
        Assert.Equal(1, filtered.Total);
        Assert.Equal("旅行手記", filtered.Hits[0].Title);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(2, (await search.SearchAsync("咖啡", year: now.Year, month: now.Month)).Total);
        Assert.Equal(0, (await search.SearchAsync("咖啡", year: now.Year - 1)).Total);
        await Assert.ThrowsAsync<CmsException>(() => search.SearchAsync("咖啡", month: 13));

        // 新設定的邊界：評論深度 1–10、預設介面語言須為基本語言標記格式。
        var settings = await admin.Settings.GetAsync();
        settings.NotFoundTitle = "迷路了嗎？";
        settings.NotFoundBody = "這一頁不存在。";
        settings.CommentMaxDepth = 0;
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));
        settings.CommentMaxDepth = 4;
        settings.DefaultUiLanguage = "not a language!";
        await Assert.ThrowsAsync<CmsException>(() => admin.Settings.SaveAsync(settings));
        settings.DefaultUiLanguage = "en";
        var saved = await admin.Settings.SaveAsync(settings);
        Assert.Equal("迷路了嗎？", saved.NotFoundTitle);
        Assert.Equal(4, saved.CommentMaxDepth);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var missing = await client.GetAsync("/no-such-page-xyz");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var missingHtml = WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync());
        Assert.True(missingHtml.Contains("迷路了嗎？", StringComparison.Ordinal),
            $"自訂 404 文案未輸出；HTTP {(int)missing.StatusCode}；Web exceptions: {string.Join(", ", site.WarningExceptionTypes)}；API exceptions: {string.Join(", ", site.ApiErrorTypes)}");
        Assert.Contains("這一頁不存在。", missingHtml, StringComparison.Ordinal);

        // 站點預設介面語言：沒有 ?lang= 與 Cookie 的訪客改用英文介面。
        var home = await client.GetStringAsync("/");
        Assert.Contains("<html lang=\"en\"", home, StringComparison.Ordinal);

        var searchHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/search?q=" + Uri.EscapeDataString("咖啡")));
        Assert.Contains("<mark>咖啡</mark>", searchHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"category\"", searchHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"month\"", searchHtml, StringComparison.Ordinal);
        var filteredHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/search?q=" + Uri.EscapeDataString("咖啡") + "&category=" + Uri.EscapeDataString(category.Slug ?? "")));
        Assert.Contains("旅行手記", filteredHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("程式設計與咖啡", filteredHtml, StringComparison.Ordinal);
        foreach (var month in new[] { "2026-13", "2026/02", "+026-02", "2026-2" })
        {
            using var badMonth = await client.GetAsync("/search?q=" + Uri.EscapeDataString("咖啡") + "&month=" + Uri.EscapeDataString(month));
            Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
        }

        settings = await admin.Settings.GetAsync();
        settings.NotFoundTitle = "<b>自訂404</b>";
        settings.NotFoundBody = "<img src=x onerror=alert(1)>";
        await admin.Settings.SaveAsync(settings);
        using var escapedMissing = await client.GetAsync("/missing-escaped-copy");
        Assert.Equal(HttpStatusCode.NotFound, escapedMissing.StatusCode);
        var escapedHtml = await escapedMissing.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(escapedHtml);
        Assert.Contains("<b>自訂404</b>", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("<img src=x onerror=alert(1)>", decodedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>自訂404</b>", escapedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", escapedHtml, StringComparison.Ordinal);

        // 成員工具列與維護模式預覽都必須在每次載入時解析成員身分。
        using var memberClient = site.CreateClient();
        using var memberLogin = await HttpSecurityTests.LoginAsync(memberClient, password);
        Assert.Equal(HttpStatusCode.Redirect, memberLogin.StatusCode);
        using var memberHome = await memberClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, memberHome.StatusCode);
        var memberDocument = new HtmlParser().ParseDocument(await memberHome.Content.ReadAsStringAsync());
        Assert.NotNull(memberDocument.QuerySelector(".member-bar"));

        settings = await admin.Settings.GetAsync();
        settings.MaintenanceMode = true;
        settings.MaintenanceMessage = "維護模式回歸檢查";
        await admin.Settings.SaveAsync(settings);
        using var maintenanceVisitor = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, maintenanceVisitor.StatusCode);
        Assert.Contains("維護模式回歸檢查", WebUtility.HtmlDecode(await maintenanceVisitor.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        using var maintenanceMember = await memberClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, maintenanceMember.StatusCode);
        var maintenanceMemberDocument = new HtmlParser().ParseDocument(await maintenanceMember.Content.ReadAsStringAsync());
        Assert.NotNull(maintenanceMemberDocument.QuerySelector(".member-bar"));
        Assert.NotNull(maintenanceMemberDocument.QuerySelector(".notice"));
    }
}
