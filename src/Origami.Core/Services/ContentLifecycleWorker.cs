namespace Origami.Core.Services;

/// <summary>
/// 到期時發布排程草稿並清理逾期回收桶。兩者都是資料庫上的冪等系統工作；
/// 失敗的輪次會在下個週期重試，絕不會拖垮主機。
/// </summary>
public sealed partial class ContentLifecycleWorker(
    IServiceProvider services,
    TimeProvider clock,
    ILogger<ContentLifecycleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PromoteInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var promoteTimer = new PeriodicTimer(PromoteInterval, clock);
        var nextPurge = DateTimeOffset.MinValue; // 啟動後不久先執行一次清理
        try
        {
            while (true)
            {
                await RunPassAsync(stoppingToken);
                if (clock.GetUtcNow() >= nextPurge)
                {
                    nextPurge = clock.GetUtcNow() + PurgeInterval;
                    await RunPurgeAsync(stoppingToken);
                }
                await promoteTimer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* 主機關閉會取消迴圈。 */ }
    }

    /// <summary>發布到期排程；供測試與單次執行使用。</summary>
    public Task<int> PromoteAsync(CancellationToken ct = default) => WithContentServiceAsync((content, token) => content.PromoteDueAsync(token), ct);

    /// <summary>清理逾期回收桶；供測試與單次執行使用。</summary>
    public Task<int> PurgeAsync(CancellationToken ct = default) => WithContentServiceAsync((content, token) => content.PurgeExpiredTrashAsync(token), ct);

    private async Task RunPassAsync(CancellationToken ct)
    {
        try { await PromoteAsync(ct); }
        catch (Exception error) when (error is not OperationCanceledException) { Log.PromoteFailed(logger, error); }
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        try { await PurgeAsync(ct); }
        catch (Exception error) when (error is not OperationCanceledException) { Log.PurgeFailed(logger, error); }
    }

    private async Task<int> WithContentServiceAsync(Func<ContentService, CancellationToken, Task<int>> operation, CancellationToken ct)
    {
        // 系統操作解析 scoped 服務；絕不需要管理員主體。
        using var scope = services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<ContentService>();
        return await operation(content, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Warning, "排程發布輪詢失敗，下次輪詢將重試。")]
        public static partial void PromoteFailed(ILogger logger, Exception exception);

        [LoggerMessage(LogLevel.Warning, "回收桶清理失敗，下次輪詢將重試。")]
        public static partial void PurgeFailed(ILogger logger, Exception exception);
    }
}
