namespace Origami.Web.Services;

/// <summary>
/// Regenerates responsive variant files that predate variant generation. Runs once after startup,
/// never blocks boot, and failures only mean the original image keeps serving on its own.
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
            catch (OperationCanceledException) { /* Shutdown raced the one-off backfill. */ }
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
        catch (OperationCanceledException) { /* Shutdown during startup backfill. */ }
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
