namespace Origami.Core.Intelligence;

/// <summary>Configuration completeness, not a provider connectivity or model compatibility check.</summary>
public sealed record AiStatus(
    bool EmbeddingConfigured,
    bool AssistantConfigured,
    int PublishedPosts,
    int IndexedPosts,
    int PendingJobs,
    int RetryJobs,
    DateTimeOffset? EarliestRetryAt);

public sealed class AiStatusService(
    AdminAccess admin, IDbContextFactory<CmsDbContext> factory, IOptionsMonitor<AiOptions> options)
{
    public async Task<AiStatus> GetAsync(CancellationToken ct = default)
    {
        try
        {
            await admin.RequireAsync(ct);
            var settings = options.CurrentValue;
            var embeddingConfigured = Configured(settings.Embedding?.Endpoint, settings.Embedding?.Model, settings.Embedding?.ApiKey);
            var assistantConfigured = Configured(settings.Assistant?.Endpoint, settings.Assistant?.Model, settings.Assistant?.ApiKey);
            await using var db = await factory.CreateDbContextAsync(ct);
            var posts = db.Contents.AsNoTracking()
                .Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published);
            var published = await posts.CountAsync(ct);
            // Count articles, not chunks. Old vectors with a replacement job are not ready.
            var indexed = await posts.CountAsync(x =>
                db.ContentEmbeddings.Any(e => e.ContentId == x.Id)
                && !db.EmbeddingJobs.Any(j => j.ContentId == x.Id), ct);
            // The outbox contains both post and page jobs; retries are a subset of pending jobs.
            var jobs = await db.EmbeddingJobs.AsNoTracking().GroupBy(_ => 1)
                .Select(g => new
                {
                    Pending = g.Count(),
                    Retries = g.Count(x => x.Attempts > 0),
                    EarliestRetry = g.Min(x => x.Attempts > 0 ? (DateTimeOffset?)x.NextAttemptAt : null)
                }).SingleOrDefaultAsync(ct);
            return new(embeddingConfigured, assistantConfigured, published, indexed,
                jobs?.Pending ?? 0, jobs?.Retries ?? 0, jobs?.EarliestRetry);
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never attach/log configuration values, connection details, or provider exceptions.
            throw new CmsException("目前無法讀取 AI 狀態，請稍後重試。");
        }
    }

    private static bool Configured(string? endpoint, string? model, string? key) =>
        HasValue(model) && HasValue(key)
        && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.IsWellFormedOriginalString()
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrEmpty(uri.Host) && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}
