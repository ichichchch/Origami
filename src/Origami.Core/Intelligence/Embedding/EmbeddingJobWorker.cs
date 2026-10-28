namespace Origami.Core.Intelligence.Embedding;

/// <summary>
/// 消化 EmbeddingJobs outbox 資料表中的工作。工作只在向量寫入完成後才刪除，
/// 因此嵌入供應商無法使用時，發布也不會遺失。
/// </summary>
public sealed partial class EmbeddingJobWorker : BackgroundService
{
    private readonly IServiceProvider services;
    private readonly TimeProvider clock;
    private readonly ILogger<EmbeddingJobWorker> logger;
    private readonly IDbContextFactory<CmsDbContext>? defaultFactory;
    private const int MaxJobsPerSitePass = 1000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)];

    // Hosted 註冊必須明確選此建構子；一般 DI 的最長建構子選擇不採用此屬性。
    [ActivatorUtilitiesConstructor]
    public EmbeddingJobWorker(IServiceProvider services, TimeProvider clock, ILogger<EmbeddingJobWorker> logger)
    {
        this.services = services;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// 保留舊測試的直接建構方式；備援 factory 只供未註冊站點的預設站測試。
    /// 僅供測試使用因此為 internal：DI 只考慮 public 建構子，
    /// 避免單例背景服務被驗證為可注入 scoped 的 factory。
    /// </summary>
    internal EmbeddingJobWorker(IDbContextFactory<CmsDbContext> factory, IServiceProvider services,
        TimeProvider clock, ILogger<EmbeddingJobWorker> logger) : this(services, clock, logger)
    {
        defaultFactory = factory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, clock);
        try
        {
            while (true)
            {
                await RunPassAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* 主機關閉會取消輪詢迴圈；無需清理。 */ }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        try { await ProcessPendingAsync(ct); }
        // 單次處理失敗絕不能讓背景服務終止；下次輪詢會重試。
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.PassFailed(logger, error.GetType().Name);
        }
    }

    /// <summary>逐啟用站點處理到期工作，每站每輪上限 1000 筆；供測試與一次性執行使用。</summary>
    public Task<int> ProcessPendingAsync(CancellationToken ct = default) =>
        SiteWorkRunner.RunAsync(services, "嵌入工作", (siteServices, factory, token) =>
            ProcessSitePendingAsync(factory, siteServices, token), logger, ct, defaultFactory);

    private async Task<int> ProcessSitePendingAsync(IDbContextFactory<CmsDbContext> factory,
        IServiceProvider scopedServices, CancellationToken ct)
    {
        List<Guid> due;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var now = clock.GetUtcNow();
            due = await db.EmbeddingJobs.AsNoTracking()
                .Where(x => x.NextAttemptAt <= now).OrderBy(x => x.EnqueuedAt).ThenBy(x => x.Id)
                .Select(x => x.Id).Take(MaxJobsPerSitePass).ToListAsync(ct);
        }
        var processed = 0;
        foreach (var jobId in due)
        {
            ct.ThrowIfCancellationRequested();
            // 供應商失敗只暫停此站本輪；其他站點仍會取得自己的 scope 與執行機會。
            if (!await TryProcessAsync(factory, scopedServices, jobId, ct)) break;
            processed++;
        }
        return processed;
    }

    private async Task<bool> TryProcessAsync(IDbContextFactory<CmsDbContext> factory,
        IServiceProvider scopedServices, Guid jobId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.EmbeddingJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
        if (job is null) return true;
        var content = await db.Contents.AsNoTracking().Where(x => x.Id == job.ContentId)
            .Select(x => new { x.Status, x.IsPrivate, x.IsProtected, x.Markdown }).SingleOrDefaultAsync(ct);
        // 已撤回或已刪除的內容不得留下任何向量；撤回時已在自身的交易中移除，
        // 因此這裡是防禦性清除並順帶清理工作。
        if (content is null || content.Status != ContentStatus.Published)
        {
            db.EmbeddingJobs.Remove(job);
            if (content is not null)
                db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == job.ContentId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return true;
        }
        // 私密與密碼保護內容絕不送達嵌入供應商。可見性旗標與文字來自同一次讀取，
        // 因此受管制的文字無法外流。
        if (content.IsPrivate || content.IsProtected)
        {
            db.EmbeddingJobs.Remove(job);
            db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == job.ContentId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return true;
        }
        var chunks = MarkdownChunker.Chunk(content.Markdown);
        if (chunks.Count == 0)
        {
            db.EmbeddingJobs.Remove(job);
            await db.SaveChangesAsync(ct);
            return true;
        }
        IReadOnlyList<float[]> vectors;
        try
        {
            // gateway 與 factory 共用已設定站點的 scope，不建立回到預設站的新 scope。
            var gateway = scopedServices.GetRequiredService<IEmbeddingGateway>();
            vectors = await gateway.EmbedBatchAsync(chunks, ct);
            if (vectors.Count != chunks.Count) throw new InvalidOperationException("嵌入結果數量與切塊數量不符。");
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            job.Attempts++;
            var delay = Backoff[Math.Min(job.Attempts - 1, Backoff.Length - 1)];
            job.NextAttemptAt = clock.GetUtcNow() + delay;
            await db.SaveChangesAsync(ct);
            // 例外本身可能含供應商回應或請求正文；日誌僅記錄型別及非機密工作資訊。
            Log.JobFailed(logger, error.GetType().Name, db.CurrentSiteId, job.ContentId,
                job.Attempts, delay.ToString(@"dd\ hh\:mm"));
            return false;
        }
        // 內容可能在供應商回應期間轉為私密；這類文字不進入索引，
        // 剛計算出的向量也再次丟棄。
        var stillPublic = await db.Contents.AsNoTracking().Where(x => x.Id == job.ContentId)
            .Select(x => x.Status == ContentStatus.Published && !x.IsPrivate && !x.IsProtected).SingleOrDefaultAsync(ct);
        if (!stillPublic)
        {
            db.EmbeddingJobs.Remove(job);
            db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == job.ContentId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            return true;
        }
        // 向量與工作資料列在同一交易內替換：讀取者不會看到重新嵌入到一半的狀態。
        db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == job.ContentId).ToListAsync(ct));
        for (var i = 0; i < chunks.Count; i++)
            db.ContentEmbeddings.Add(new ContentEmbedding { SiteId = db.CurrentSiteId, ContentId = job.ContentId, ChunkIndex = i, ChunkText = chunks[i], Embedding = new Vector(vectors[i]) });
        db.EmbeddingJobs.Remove(job);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Warning, "嵌入工作批次失敗（{ErrorType}），將於下次輪詢重試。")]
        public static partial void PassFailed(ILogger logger, string errorType);

        [LoggerMessage(LogLevel.Warning, "站點 {SiteId} 內容 {ContentId} 的嵌入工作失敗（{ErrorType}），第 {Attempts} 次將於 {Delay} 後重試。")]
        public static partial void JobFailed(ILogger logger, string errorType, Guid siteId, Guid contentId, int attempts, string delay);
    }
}
