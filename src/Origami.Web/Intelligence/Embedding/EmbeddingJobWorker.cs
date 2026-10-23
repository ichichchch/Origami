namespace Origami.Web.Intelligence.Embedding;

/// <summary>
/// Drains the EmbeddingJobs outbox table. A job is deleted only after its vectors are
/// written, so a publish can never be lost when the embedding provider is unavailable.
/// </summary>
public sealed class EmbeddingJobWorker(
    IDbContextFactory<CmsDbContext> factory,
    IServiceProvider services,
    TimeProvider clock,
    ILogger<EmbeddingJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)];

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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        try { await ProcessPendingAsync(ct); }
        // A failed pass must never kill the hosted service; the next tick retries.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "嵌入工作批次失敗，將於下次輪詢重試。");
        }
    }

    /// <summary>Processes due jobs until none remain; exposed for tests and one-off runs.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
    {
        var processed = 0;
        while (!ct.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            await using var db = await factory.CreateDbContextAsync(ct);
            var due = await db.EmbeddingJobs.AsNoTracking()
                .Where(x => x.NextAttemptAt <= now).OrderBy(x => x.EnqueuedAt).ThenBy(x => x.Id)
                .Select(x => x.Id).FirstOrDefaultAsync(ct);
            if (due == Guid.Empty) break;
            if (!await TryProcessAsync(due, ct)) break;
            processed++;
        }
        return processed;
    }

    private async Task<bool> TryProcessAsync(Guid jobId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.EmbeddingJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
        if (job is null) return true;
        var content = await db.Contents.AsNoTracking().Where(x => x.Id == job.ContentId)
            .Select(x => new { x.Status, x.Markdown }).SingleOrDefaultAsync(ct);
        // Withdrawn or deleted content must leave no vectors behind; withdrawal already removed
        // them in its own transaction, so this is a defensive purge plus job cleanup.
        if (content is null || content.Status != ContentStatus.Published)
        {
            db.EmbeddingJobs.Remove(job);
            if (content is not null)
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
        using (var scope = services.CreateScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<IEmbeddingGateway>();
            try { vectors = await gateway.EmbedBatchAsync(chunks, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                job.Attempts++;
                var delay = Backoff[Math.Min(job.Attempts - 1, Backoff.Length - 1)];
                job.NextAttemptAt = clock.GetUtcNow() + delay;
                await db.SaveChangesAsync(ct);
                // Only the retry count and timing are logged; the provider error text stays out.
                logger.LogWarning("內容 {ContentId} 的嵌入工作失敗，第 {Attempts} 次將於 {Delay:dd\\ HH:mm} 後重試。", job.ContentId, job.Attempts, delay);
                return false;
            }
        }
        if (vectors.Count != chunks.Count) throw new InvalidOperationException("嵌入結果數量與切塊數量不符。");
        // Vectors and the job row are replaced in one transaction: readers never see half of a re-embed.
        db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == job.ContentId).ToListAsync(ct));
        for (var i = 0; i < chunks.Count; i++)
            db.ContentEmbeddings.Add(new ContentEmbedding { ContentId = job.ContentId, ChunkIndex = i, ChunkText = chunks[i], Embedding = new Vector(vectors[i]) });
        db.EmbeddingJobs.Remove(job);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
