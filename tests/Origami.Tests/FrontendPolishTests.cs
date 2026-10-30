namespace Origami.Tests;

/// <summary>
/// 前台體驗細節：投機載入規則。跨頁轉場（@view-transition）經實測在目前 Edge
/// 會對每次導航拋出 InvalidStateError（opt-in disabled），違反瀏覽器測試「控制台
/// 零錯誤」的既有契約，故暫不啟用，待瀏覽器穩定支援後再評估。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FrontendPolishTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Public_pages_emit_conservative_speculation_rules()
    {
        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var html = await client.GetStringAsync("/");
        // 同源保守預渲染：排除後台、帳號與預覽路徑；規則存在於版面層（Static SSR 輸出內文）。
        Assert.Contains("speculationrules", html);
        Assert.Contains("\"prerender\"", html);
        Assert.Contains("\"eagerness\":\"conservative\"", html);
        Assert.Contains("/admin*", html);
        Assert.DoesNotContain("\"eagerness\":\"immediate\"", html);
    }
}
