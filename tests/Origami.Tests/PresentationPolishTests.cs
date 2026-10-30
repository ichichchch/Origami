namespace Origami.Tests;

/// <summary>呈現細節包：Feed 全文與作者名、搜尋分頁、記住我、apple-touch-icon。</summary>
[Collection(PostgresCollection.Name)]
public sealed class PresentationPolishTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password, bool remember)
    {
        var form = new HtmlParser().ParseDocument(await client.GetStringAsync("/account/login")).QuerySelector("form");
        Assert.NotNull(form);
        var fields = form.QuerySelectorAll("input[type='hidden'][name]")
            .ToDictionary(x => x.GetAttribute("name")!, x => x.GetAttribute("value") ?? "");
        fields["Input.Username"] = "test-admin";
        fields["Input.Password"] = password;
        fields["Input.Remember"] = remember ? "true" : "false";
        return await client.PostAsync("/account/login", new FormUrlEncodedContent(fields));
    }

    [PostgresFact]
    public async Task Login_environment_defaults_to_public_and_home_issues_a_thirty_day_cookie()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();

        using var sessionClient = site.CreateClient();
        var loginDocument = new HtmlParser().ParseDocument(await sessionClient.GetStringAsync("/account/login"));
        var environments = loginDocument.QuerySelectorAll("input[type='radio'][name='Input.Remember']");
        Assert.Equal(2, environments.Length);
        Assert.Equal("false", Assert.Single(environments, input => input.HasAttribute("checked")).GetAttribute("value"));
        Assert.Empty(loginDocument.QuerySelectorAll("button[formaction='/account/external/challenge']"));
        using var ephemeral = await LoginAsync(sessionClient, password, remember: false);
        Assert.Equal(HttpStatusCode.Redirect, ephemeral.StatusCode);
        var ephemeralCookie = ephemeral.Headers.GetValues("Set-Cookie")
            .Single(c => c.Contains("__Host-Origami.Admin", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", ephemeralCookie, StringComparison.OrdinalIgnoreCase);

        using var persistentClient = site.CreateClient();
        var before = DateTimeOffset.UtcNow;
        using var persistent = await LoginAsync(persistentClient, password, remember: true);
        var after = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Redirect, persistent.StatusCode);
        var persistentCookie = persistent.Headers.GetValues("Set-Cookie")
            .Single(c => c.Contains("__Host-Origami.Admin", StringComparison.Ordinal));
        var parsed = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(persistentCookie);
        Assert.NotNull(parsed.Expires);
        Assert.InRange(parsed.Expires.Value, before.AddDays(30).AddSeconds(-1), after.AddDays(30));
        Assert.True(parsed.Secure);
        Assert.True(parsed.HttpOnly);

        await App.Provisioner.ResetPasswordAsync(IsolatedPostgres.NewPassword());
    }

    [PostgresFact]
    public async Task Feed_full_text_and_author_byline_follow_settings()
    {
        using var admin = await App.AdministratorAsync();
        var post = NewContent("全文示範文章", status: ContentStatus.Published);
        post.Markdown = "開頭段落\n\n**粗體全文內容**";
        await admin.Content.SaveAsync(post);
        var settings = await admin.Settings.GetAsync();
        settings.FeedShowFullText = true;
        settings.AuthorDisplayName = "單文浩";
        await admin.Settings.SaveAsync(settings);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        using var rss = await client.GetAsync("/feed");
        var rssXml = WebUtility.HtmlDecode(await rss.Content.ReadAsStringAsync());
        Assert.Contains("content:encoded", rssXml, StringComparison.Ordinal);
        Assert.Contains("<strong>粗體全文內容</strong>", rssXml, StringComparison.Ordinal);
        // RSS 2.0 頻道編者欄位依規範為 managingEditor（Atom 側才是 author）。
        Assert.Contains("managingEditor", rssXml, StringComparison.Ordinal);
        Assert.Contains("單文浩", rssXml, StringComparison.Ordinal);

        using var atom = await client.GetAsync("/feed/atom");
        var atomXml = WebUtility.HtmlDecode(await atom.Content.ReadAsStringAsync());
        Assert.Contains("type=\"html\"", atomXml, StringComparison.Ordinal);
        Assert.Contains("<strong>粗體全文內容</strong>", atomXml, StringComparison.Ordinal);
        Assert.Contains("單文浩", atomXml, StringComparison.Ordinal);

        using var detail = await client.GetAsync("/posts/全文示範文章");
        var html = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Contains("作者 · 單文浩", html, StringComparison.Ordinal);

        // 關閉全文後 content:encoded 消失，摘要不含渲染後的 HTML。
        var off = await admin.Settings.GetAsync();
        off.FeedShowFullText = false;
        await admin.Settings.SaveAsync(off);
        using var summary = await client.GetAsync("/feed");
        var summaryXml = WebUtility.HtmlDecode(await summary.Content.ReadAsStringAsync());
        Assert.DoesNotContain("content:encoded", summaryXml, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>粗體全文內容</strong>", summaryXml, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Search_results_paginate_with_totals()
    {
        using var admin = await App.AdministratorAsync();
        for (var i = 1; i <= 12; i++)
        {
            var post = NewContent($"分頁文章{i:00}", status: ContentStatus.Published);
            post.Markdown = "共通關鍵詞批次甲第" + i + "篇";
            await admin.Content.SaveAsync(post);
        }
        var search = admin.Services.GetRequiredService<PublicSearchService>();
        var page1 = await search.SearchAsync("共通關鍵詞", limit: 10);
        Assert.Equal(12, page1.Total);
        Assert.Equal(10, page1.Hits.Count);
        var page2 = await search.SearchAsync("共通關鍵詞", limit: 10, page: 2);
        Assert.Equal(2, page2.Hits.Count);
        // 第二頁起是純關鍵字檢索：語意未呼叫，也不誤報「無法使用」。
        Assert.All(page2.Hits, hit => Assert.Equal("關鍵字", hit.Source));
        Assert.True(page2.SemanticAvailable);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var second = await client.GetAsync("/search?q=" + Uri.EscapeDataString("共通關鍵詞") + "&page=2");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = WebUtility.HtmlDecode(await second.Content.ReadAsStringAsync());
        Assert.Contains("共 12 篇符合", body, StringComparison.Ordinal);
        Assert.Contains("第 2 / 2 頁", body, StringComparison.Ordinal);

        using var zero = await client.GetAsync("/search?q=" + Uri.EscapeDataString("共通關鍵詞") + "&page=0");
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        using var beyond = await client.GetAsync("/search?q=" + Uri.EscapeDataString("共通關鍵詞") + "&page=99");
        Assert.Equal(HttpStatusCode.NotFound, beyond.StatusCode);
    }

    [PostgresFact]
    public async Task Apple_touch_icon_link_present_and_404_without_png_icon()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        using var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("apple-touch-icon", html, StringComparison.Ordinal);
        // 未設定網站圖示（或圖示非 PNG）時，慣例網址回 404 而非提供錯誤格式。
        using var touch = await client.GetAsync("/apple-touch-icon.png");
        Assert.Equal(HttpStatusCode.NotFound, touch.StatusCode);
    }
}
