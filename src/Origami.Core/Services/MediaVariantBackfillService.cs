namespace Origami.Core.Services;

/// <summary>
/// 重新產生變體功能出現前便已存在的回應式變體檔。啟動後執行一次、
/// 絕不阻塞開機，失敗只代表原圖繼續自行提供服務。
/// </summary>
public sealed partial class MediaVariantBackfillService(
    IServiceProvider services,
    ILogger<MediaVariantBackfillService> logger) : IHostedService
{
    private Task? _backfill;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _backfill = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_backfill is not null)
        {
            try { await _backfill; }
            catch (OperationCanceledException) { /* 關閉與單次回填相互競爭。 */ }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var repaired = await scope.ServiceProvider.GetRequiredService<MediaService>().BackfillVariantsAsync(ct);
            if (repaired > 0) Log.Repaired(logger, repaired);
        }
        catch (OperationCanceledException) { /* 啟動回填期間遇到關閉。 */ }
        catch (Exception error) { Log.Failed(logger, error); }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "已補齊 {Count} 個圖片縮圖變體。")]
        public static partial void Repaired(ILogger logger, int count);

        [LoggerMessage(LogLevel.Warning, "圖片縮圖回填失敗；原图仍會正常提供。")]
        public static partial void Failed(ILogger logger, Exception exception);
    }
}
