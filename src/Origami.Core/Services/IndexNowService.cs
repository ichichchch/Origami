namespace Origami.Core.Services;

/// <summary>
/// IndexNow 搜尋引擎推送：內容發布時把站點絕對網址提交到 IndexNow 端點（Bing 等採用）。
/// 盡力而為：未啟用、金鑰無效、站點未登記網域或提交失敗都只記日誌，絕不影響發布流程。
/// 金鑰驗證檔（{key}.txt）由網站主機按目前站點設定動態提供。
/// </summary>
public sealed partial class IndexNowService(
    IDbContextFactory<CmsDbContext> factory,
    IHttpClientFactory? httpClients = null,
    ILogger<IndexNowService>? logger = null)
{
    public const string Endpoint = "https://api.indexnow.org/indexnow";

    [GeneratedRegex(@"^[0-9a-fA-F]{8,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string? key) => !string.IsNullOrEmpty(key) && KeyPattern().IsMatch(key);

    /// <summary>內容首次發布時提交其公開網址；站點未啟用或設定不完整時靜默略過。</summary>
    public async Task NotifyPublishedAsync(Guid siteId, ContentKind kind, string slug, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (settings is null || !settings.IndexNowEnabled || !IsValidKey(settings.IndexNowKey)) return;
            var host = await db.Sites.AsNoTracking().Where(x => x.Id == db.CurrentSiteId)
                .Select(x => x.HostName).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(host)) return;
            var url = $"https://{host}/" + (kind == ContentKind.Page ? "pages/" : "posts/") + Uri.EscapeDataString(slug);
            using var client = httpClients is not null
                ? httpClients.CreateClient("OrigamiIndexNow")
                : new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var body = new StringContent(
                $$"""{"host":"{{host}}","key":"{{settings.IndexNowKey}}","urlList":["{{url}}"]}""",
                Encoding.UTF8, "application/json; charset=utf-8");
            using var response = await client.PostAsync(Endpoint, body, ct);
            if (!response.IsSuccessStatusCode)
                logger?.LogWarning("IndexNow 提交失敗（HTTP {Status}）：{Url}", (int)response.StatusCode, url);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger?.LogWarning(error, "IndexNow 提送未完成；不影響發布流程。");
        }
    }
}
