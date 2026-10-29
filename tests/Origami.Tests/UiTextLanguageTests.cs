namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class UiTextLanguageTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Pages_switch_languages_and_remember_the_selection()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();

        var defaultHtml = await PageAsync(client, "/search");
        Assert.Contains("語意搜尋", defaultHtml);
        Assert.Contains("<html lang=\"zh-Hant\"", defaultHtml);

        using var englishPage = await client.GetAsync("/search?lang=EN");
        englishPage.EnsureSuccessStatusCode();
        var englishHtml = WebUtility.HtmlDecode(await englishPage.Content.ReadAsStringAsync());
        Assert.Contains("Semantic search", englishHtml);
        Assert.DoesNotContain("語意搜尋", englishHtml);
        Assert.Contains("<html lang=\"en\"", englishHtml);
        Assert.Contains("origami.lang=en", string.Join(",", englishPage.Headers.GetValues("Set-Cookie")));

        Assert.Contains("Semantic search", await PageAsync(client, "/search"));
        Assert.Contains("Semantic search", await PageAsync(client, "/search?lang=zz-ZZ"));
        Assert.Contains("語意搜尋", await PageAsync(client, "/search?lang=zh-Hant"));
        Assert.Contains("語意搜尋", await PageAsync(client, "/search"));
    }

    [PostgresFact]
    public async Task Unsupported_language_does_not_create_a_cookie_or_inherit_another_request_culture()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var englishClient = site.CreateClient();
        using var defaultClient = site.CreateClient();
        await PageAsync(englishClient, "/search?lang=en");

        using var page = await defaultClient.GetAsync("/search?lang=zz-ZZ");
        page.EnsureSuccessStatusCode();
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("語意搜尋", html);
        Assert.Contains("<html lang=\"zh-Hant\"", html);
        if (page.Headers.TryGetValues("Set-Cookie", out var cookies))
            Assert.DoesNotContain(cookies, value => value.StartsWith("origami.lang=", StringComparison.Ordinal));

        var simultaneous = await Task.WhenAll(PageAsync(englishClient, "/search"), PageAsync(defaultClient, "/search"));
        Assert.Contains("<html lang=\"en\"", simultaneous[0]);
        Assert.Contains("<html lang=\"zh-Hant\"", simultaneous[1]);
    }

    [PostgresFact]
    public async Task Language_links_preserve_search_filters_and_the_logo_remains_visible()
    {
        // 搜尋有兩頁才會有合法的第 2 頁；否則該頁依規則回 404。
        using (var admin = await App.AdministratorAsync())
        {
            for (var index = 0; index <= Limits.PageSize; index++)
            {
                var post = NewContent($"hello 搜尋測試 {index}", status: ContentStatus.Published);
                post.Markdown = "hello world";
                await admin.Content.SaveAsync(post);
            }
        }
        var logoFileName = Guid.NewGuid().ToString("N") + ".png";
        await using (var db = await App.CreateContextAsync())
        {
            var logo = new MediaAsset
            {
                FileName = logoFileName, OriginalName = "logo.png", ContentType = "image/png",
                Width = 100, Height = 50, UploadedAt = DateTimeOffset.UtcNow
            };
            db.MediaAssets.Add(logo);
            await db.SaveChangesAsync();
            var settings = await db.Settings.SingleAsync();
            settings.LogoMediaId = logo.Id;
            await db.SaveChangesAsync();
        }
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var html = await PageAsync(client, "/search?q=hello&page=2&lang=en");
        var document = new HtmlParser().ParseDocument(html);
        var link = document.QuerySelector(".language-switcher a[lang='zh-Hant']");
        Assert.NotNull(link);
        var uri = new Uri(site.Address, link.GetAttribute("href"));
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("hello", query["q"].ToString());
        Assert.Equal("2", query["page"].ToString());
        Assert.Equal("zh-Hant", query["lang"].ToString());
        Assert.Equal("/media/" + logoFileName, document.QuerySelector(".site-logo")?.GetAttribute("src"));
    }

    private static async Task<string> PageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}

public sealed class UiTextResourceTests
{
    [Theory]
    [InlineData("en", "Common.Processing", "原文", "Processing, please wait…")]
    [InlineData("zh-Hant", "Common.Processing", "處理中，請稍候…", "處理中，請稍候…")]
    [InlineData("fr", "Common.Processing", "原文", "原文")]
    [InlineData("en", "Public.PageOfSuffix", "頁", "")]
    [InlineData("en", "Missing.Resource", "原文", "原文")]
    public void Resources_preserve_empty_translations_and_fall_back_only_when_missing(
        string language, string key, string fallback, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, UiText.T(key, fallback));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("EN", "en")]
    [InlineData("zh-hant", "zh-Hant")]
    [InlineData("en-US", null)]
    [InlineData("../../en", null)]
    [InlineData("en,zh-Hant", null)]
    [InlineData(null, null)]
    public void Only_explicitly_supported_cultures_are_normalized(string? input, string? expected) =>
        Assert.Equal(expected, UiTextLanguages.Normalize(input));
}
