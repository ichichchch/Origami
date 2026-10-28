namespace Origami.Core.Services;

/// <summary>每日自動備份的設定；全部由外部組態提供，預設關閉。</summary>
public sealed class BackupOptions
{
    /// <summary>是否啟用每日自動備份；預設關閉。</summary>
    public bool Enabled { get; set; }
    /// <summary>保留份數上限，超出時刪除最舊；至少保留 1 份。</summary>
    public int Keep { get; set; } = 7;
    /// <summary>每日執行的 UTC 小時（0–23）。</summary>
    public int HourUtc { get; set; } = 3;
}

public sealed record BackupResult(string Path, long TotalBytes, int Retained);

/// <summary>
/// 把「JSON＋媒體」完整封裝寫入當站儲存目錄下的 backups/，先落暫存檔再原子改名，
/// 並保留最新幾份。備份屬系統操作，不經管理員授權（匯出核心不帶授權檢查）。
/// </summary>
public sealed partial class BackupService(ExportService export, StoragePaths storage, TimeProvider clock, ILogger<BackupService> logger)
{
    public string BackupsDirectory => Path.Combine(storage.SiteRoot, "backups");

    public async Task<BackupResult> RunAsync(BackupOptions options, CancellationToken ct = default)
    {
        var keep = Math.Max(1, options.Keep);
        var directory = BackupsDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            "origami-backup-" + clock.GetUtcNow().ToString("yyyyMMdd-HHmmss") + ".zip");
        var staging = path + ".part";
        try
        {
            await using (var file = File.Create(staging))
            {
                await export.WriteArchiveUncheckedAsync(file, ct);
            }
            // 同一秒內重複執行時直接覆蓋同名備份，不讓第二次備份失敗。
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (IOException) { /* 暫存清理盡力而為。 */ }
        }
        // 僅列出本次備份的當站目錄（不遞迴）；保留最新幾份，其餘刪除。
        var retained = Directory.EnumerateFiles(directory, "origami-backup-*.zip")
            .OrderDescending(StringComparer.Ordinal).ToList();
        foreach (var stale in retained.Skip(keep))
        {
            try { File.Delete(stale); }
            catch (IOException ex) { Log.TrimFailed(logger, ex.GetType().Name); }
        }
        var kept = retained.Take(keep).ToList();
        return new(path, kept.Sum(x => new FileInfo(x).Length), kept.Count);
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Warning, "無法刪除逾期的備份檔（{ErrorType}），將於下次備份再試。")]
        public static partial void TrimFailed(ILogger logger, string errorType);
    }
}

/// <summary>每日定時把完整封裝寫入各站的 backups/；失敗記錄後續行，不中斷服務。</summary>
public sealed partial class BackupWorker(
    IServiceProvider services, IOptions<BackupOptions> options, TimeProvider clock,
    ILogger<BackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextDelay(clock, settings.HourUtc);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
            try
            {
                await SiteWorkRunner.RunAsync(services, "每日自動備份", async (siteServices, _, token) =>
                {
                    var result = await siteServices.GetRequiredService<BackupService>().RunAsync(settings, token);
                    Log.Completed(logger, result.TotalBytes);
                    return 1;
                }, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Failed(logger, ex.GetType().Name);
            }
        }
    }

    private static TimeSpan NextDelay(TimeProvider clock, int hourUtc)
    {
        var hour = Math.Clamp(hourUtc, 0, 23);
        var now = clock.GetUtcNow();
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "每日自動備份完成（{TotalBytes} 位元組）。")]
        public static partial void Completed(ILogger logger, long totalBytes);
        [LoggerMessage(LogLevel.Error, "每日自動備份失敗（{ErrorType}），將於明日再試。")]
        public static partial void Failed(ILogger logger, string errorType);
    }
}
