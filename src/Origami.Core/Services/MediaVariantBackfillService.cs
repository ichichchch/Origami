namespace Origami.Core.Services;

/// <summary>
/// 重新產生變體功能出現前便已存在的回應式變體檔。啟動後執行一次、
/// 絕不阻塞開機，失敗只代表原圖繼續自行提供服務。
/// </summary>
public sealed partial class MediaVariantBackfillService(
    IServiceProvider services,
    ILogger<MediaVariantBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var repaired = await SiteWorkRunner.RunAsync(services, "圖片縮圖回填",
                (siteServices, _, token) => siteServices.GetRequiredService<MediaService>().BackfillVariantsAsync(token),
                logger, stoppingToken);
            if (repaired > 0) Log.Repaired(logger, repaired);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* 啟動回填期間遇到關閉。 */ }
        catch (Exception error) { Log.Failed(logger, error.GetType().Name); }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "已補齊 {Count} 個圖片縮圖變體。")]
        public static partial void Repaired(ILogger logger, int count);

        [LoggerMessage(LogLevel.Warning, "圖片縮圖回填失敗（{ErrorType}）；原图仍會正常提供。")]
        public static partial void Failed(ILogger logger, string errorType);
    }
}
