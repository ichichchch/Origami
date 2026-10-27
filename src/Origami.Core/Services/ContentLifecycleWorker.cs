namespace Origami.Core.Services;

/// <summary>
/// Promotes scheduled drafts when due and purges expired trash. Both operations are idempotent
/// system jobs on the database; a failed pass is retried on the next tick and never kills the host.
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
        var nextPurge = DateTimeOffset.MinValue; // run one purge shortly after startup
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* Host shutdown cancels the loop. */ }
    }

    /// <summary>Promotes due schedules; exposed for tests and one-off runs.</summary>
    public Task<int> PromoteAsync(CancellationToken ct = default) => WithContentServiceAsync((content, token) => content.PromoteDueAsync(token), ct);

    /// <summary>Purges expired trash; exposed for tests and one-off runs.</summary>
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
        // The system operations resolve the scoped service; they never require an administrator principal.
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
