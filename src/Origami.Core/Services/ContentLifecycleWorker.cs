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

    /// <summary>逐站發布到期排程；供測試與單次執行使用。</summary>
    public Task<int> PromoteAsync(CancellationToken ct = default) => SiteWorkRunner.RunAsync(services, "排程發布",
        (siteServices, _, token) => siteServices.GetRequiredService<ContentService>().PromoteDueAsync(token), logger, ct);

    /// <summary>逐站清理逾期回收桶；供測試與單次執行使用。</summary>
    public Task<int> PurgeAsync(CancellationToken ct = default) => SiteWorkRunner.RunAsync(services, "回收桶清理",
        (siteServices, _, token) => siteServices.GetRequiredService<ContentService>().PurgeExpiredTrashAsync(token), logger, ct);

    private async Task RunPassAsync(CancellationToken ct)
    {
        try { await PromoteAsync(ct); }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        { Log.PromoteFailed(logger, error.GetType().Name); }
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        try { await PurgeAsync(ct); }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        { Log.PurgeFailed(logger, error.GetType().Name); }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Warning, "排程發布輪詢失敗（{ErrorType}），下次輪詢將重試。")]
        public static partial void PromoteFailed(ILogger logger, string errorType);

        [LoggerMessage(LogLevel.Warning, "回收桶清理失敗（{ErrorType}），下次輪詢將重試。")]
        public static partial void PurgeFailed(ILogger logger, string errorType);
    }
}

/// <summary>只跨站讀取啟用站點識別；正文與系統操作一律在獨立的站點 scope 內執行。</summary>
internal static partial class SiteWorkRunner
{
    public static async Task<int> RunAsync(IServiceProvider services, string operation,
        Func<IServiceProvider, IDbContextFactory<CmsDbContext>, CancellationToken, Task<int>> work,
        ILogger logger, CancellationToken ct, IDbContextFactory<CmsDbContext>? defaultFactory = null)
    {
        List<Guid> siteIds;
        await using (var scope = services.CreateAsyncScope())
        {
            var factory = ResolveFactory(scope.ServiceProvider, defaultFactory);
            await using var db = await factory.CreateDbContextAsync(ct);
            siteIds = await db.Sites.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        }

        var total = 0;
        foreach (var siteId in siteIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = services.CreateAsyncScope();
                var scoped = scope.ServiceProvider;
                var site = scoped.GetService<SiteContext>();
                if (site is not null) site.SiteId = siteId;
                else if (siteId != SiteDefaults.DefaultSiteId)
                    throw new InvalidOperationException("非預設站點的背景工作必須註冊 scoped SiteContext。");

                // 必須先設定 SiteContext，才能解析 scoped factory 及其依賴服務。
                // 舊測試沒註冊 SiteContext 時僅允許預設站，絕不改用未過濾查詢。
                var factory = ResolveFactory(scoped, site is null ? defaultFactory : null);
                await using (var db = await factory.CreateDbContextAsync(ct))
                {
                    if (db.CurrentSiteId != siteId)
                        throw new InvalidOperationException("背景工作的 DbContext 與站點 scope 不一致。");
                    if (!await db.Sites.AsNoTracking().AnyAsync(x => x.Id == siteId && x.IsActive, ct)) continue;
                }
                total += await work(scoped, factory, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // 不記錄例外訊息或堆疊：供應商／資料庫例外可能含正文、連線或憑證。
                Log.SiteFailed(logger, operation, siteId, error.GetType().Name);
            }
        }
        return total;
    }

    private static IDbContextFactory<CmsDbContext> ResolveFactory(IServiceProvider services,
        IDbContextFactory<CmsDbContext>? defaultFactory) =>
        services.GetService<IDbContextFactory<CmsDbContext>>() ?? defaultFactory
        ?? throw new InvalidOperationException("背景工作必須註冊 DbContext factory。");

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Warning, "站點 {SiteId} 的{Operation}失敗（{ErrorType}）；其他站點繼續執行。")]
        public static partial void SiteFailed(ILogger logger, string operation, Guid siteId, string errorType);
    }
}
