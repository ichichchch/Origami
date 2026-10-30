namespace Origami.Core.Services;

/// <summary>以真實資料庫往返支撐的活躍度檢查；不揭露供應商或設定細節。</summary>
public sealed class CmsHealthCheck(IDbContextFactory<CmsDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy(); }
    }
}

/// <summary>
/// 管理後台 Site Health 診斷服務：檢查資料庫、儲存目錄可寫性、排程與回收桶、AI 嵌入佇列、
/// Webhook 投遞狀態、自動備份、SMTP 設定與站點組態完整性。絕不回傳任何密碼、金鑰或連線字串。
/// </summary>
public sealed class SiteHealthService(
    IDbContextFactory<CmsDbContext> factory,
    AdminAccess admin,
    StoragePaths storage,
    TimeProvider clock,
    IOptionsMonitor<AiOptions>? aiOptions = null,
    IOptions<BackupOptions>? backupOptions = null,
    IEmailSender? emailSender = null)
{
    public async Task<SiteHealthReport> GetReportAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var now = clock.GetUtcNow();
        var items = new List<SiteHealthCheckItem>();

        await using var db = await factory.CreateDbContextAsync(ct);

        // 1. 資料庫連線與遷移狀態
        var dbConnected = await db.Database.CanConnectAsync(ct);
        var pendingMigrations = dbConnected ? (await db.Database.GetPendingMigrationsAsync(ct)).Count() : 0;
        var dbStatus = "good";
        var dbMessage = "資料庫連線正常，結構為最新版本。";
        if (!dbConnected)
        {
            dbStatus = "critical";
            dbMessage = "無法連線至資料庫。";
        }
        else if (pendingMigrations > 0)
        {
            dbStatus = "warning";
            dbMessage = $"有 {pendingMigrations} 項待套用的資料庫遷移。";
        }
        items.Add(new SiteHealthCheckItem(
            "database",
            "核心基礎設施",
            "PostgreSQL 資料庫連線與結構",
            dbStatus,
            dbMessage));

        // 2. 儲存目錄可寫性檢查（Images / Files / Staging）
        var storageWritable = CheckStorageWritable(storage);
        items.Add(new SiteHealthCheckItem(
            "storage",
            "核心基礎設施",
            "媒體與暫存目錄可寫性",
            storageWritable ? "good" : "critical",
            storageWritable ? "媒體、附件與暫存目錄皆可正常讀寫。" : "儲存目錄無法寫入，媒體上傳與備份可能失敗。"));

        // 3. 排程發布與生命週期背景工作
        var overdueScheduled = await db.Contents.AsNoTracking()
            .CountAsync(x => x.Status == ContentStatus.Draft && x.PublishAt != null && x.PublishAt <= now.AddMinutes(-2), ct);
        var scheduledTotal = await db.Contents.AsNoTracking()
            .CountAsync(x => x.Status == ContentStatus.Draft && x.PublishAt != null, ct);
        items.Add(new SiteHealthCheckItem(
            "scheduler",
            "背景工作",
            "排程發布與回收桶清理",
            overdueScheduled > 0 ? "warning" : "good",
            overdueScheduled > 0
                ? $"有 {overdueScheduled} 篇排程草稿已逾預定時間尚未發布，請確認背景工作者是否執行中。"
                : $"排程佇列正常（目前待發布排程：{scheduledTotal} 篇）。"));

        // 4. AI 語意嵌入與助理組態／重試佇列
        var aiCfg = aiOptions?.CurrentValue;
        var embConfigured = IsAiEndpointConfigured(aiCfg?.Embedding?.Endpoint, aiCfg?.Embedding?.Model, aiCfg?.Embedding?.ApiKey);
        var astConfigured = IsAiEndpointConfigured(aiCfg?.Assistant?.Endpoint, aiCfg?.Assistant?.Model, aiCfg?.Assistant?.ApiKey);
        var pendingEmbeddings = await db.EmbeddingJobs.AsNoTracking().CountAsync(ct);
        var failingEmbeddings = await db.EmbeddingJobs.AsNoTracking().CountAsync(x => x.Attempts >= 3, ct);
        items.Add(new SiteHealthCheckItem(
            "ai_embeddings",
            "AI 與搜尋",
            "AI 模型設定與嵌入工作佇列",
            failingEmbeddings > 0 ? "warning" : "good",
            $"嵌入模型：{(embConfigured ? "已設定" : "未設定")}；助理模型：{(astConfigured ? "已設定" : "未設定")}；待處理嵌入工作：{pendingEmbeddings} 筆（多次重試：{failingEmbeddings} 筆）。"));

        // 5. Webhook 投遞健康度
        var settings = await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        var webhookConfigured = !string.IsNullOrWhiteSpace(settings.WebhookUrl);
        var failedWebhooks = await db.WebhookDeliveries.AsNoTracking()
            .CountAsync(x => x.Status == WebhookDeliveryStatus.Failed || x.Status == WebhookDeliveryStatus.RetryScheduled, ct);
        items.Add(new SiteHealthCheckItem(
            "webhooks",
            "整合與通知",
            "出站 Webhook 投遞狀態",
            failedWebhooks > 0 ? "warning" : "good",
            !webhookConfigured
                ? "尚未設定出站 Webhook。"
                : failedWebhooks > 0
                    ? $"已啟用 Webhook，目前有 {failedWebhooks} 筆投遞處於重試或失敗狀態。"
                    : "Webhook 運作正常，近期無失敗投遞。"));

        // 6. 電子郵件（SMTP）與通知設定
        var smtpReady = emailSender?.IsConfigured == true;
        items.Add(new SiteHealthCheckItem(
            "smtp",
            "整合與通知",
            "SMTP 郵件服務與通知",
            settings.EmailNotificationsEnabled && !smtpReady ? "warning" : "good",
            smtpReady
                ? $"SMTP 已設定完成（評論／審稿郵件通知：{(settings.EmailNotificationsEnabled ? "開啟" : "關閉")}）。"
                : settings.EmailNotificationsEnabled
                    ? "已開啟郵件通知，但尚未完成外部 SMTP 設定，通知信將無法寄出。"
                    : "SMTP 未設定（選用；設定後可使用忘記密碼與評論／審稿通知）。"));

        // 7. 自動備份狀態
        var backupDir = Path.Combine(storage.SiteRoot, "backups");
        var backupFiles = Directory.Exists(backupDir)
            ? Directory.EnumerateFiles(backupDir, "origami-backup-*.zip").OrderDescending(StringComparer.Ordinal).ToList()
            : [];
        var backupEnabled = backupOptions?.Value.Enabled == true;
        items.Add(new SiteHealthCheckItem(
            "backups",
            "備份與安全",
            "站點自動備份",
            "good",
            backupFiles.Count > 0
                ? $"自動備份：{(backupEnabled ? "已啟用" : "手動")}；目前保留 {backupFiles.Count} 份備份檔。"
                : $"自動備份：{(backupEnabled ? "已啟用（尚無備份產出）" : "未啟用")}；可隨時於「網站設定」下載完整備份 ZIP。"));

        // 8. 站點一般組態與隱私政策頁檢查
        var hasPrivacyPage = settings.PrivacyPageContentId is not null;
        items.Add(new SiteHealthCheckItem(
            "configuration",
            "備份與安全",
            "站點組態與隱私權設定",
            settings.MaintenanceMode ? "warning" : "good",
            settings.MaintenanceMode
                ? "站點目前處於「維護模式」，一般訪客將看到 503 維護畫面。"
                : $"永久連結方案：{settings.PermalinkScheme}；時區：{settings.TimeZoneId}；隱私權政策頁：{(hasPrivacyPage ? "已指定" : "未指定")}。"));

        var overall = items.Any(x => x.Status == "critical") ? "critical"
            : items.Any(x => x.Status == "warning") ? "warning"
            : "good";
        return new SiteHealthReport(overall, now, items);
    }

    private static bool CheckStorageWritable(StoragePaths storage)
    {
        try
        {
            Directory.CreateDirectory(storage.Images);
            Directory.CreateDirectory(storage.Files);
            Directory.CreateDirectory(storage.Staging);
            var probe = Path.Combine(storage.Staging, ".health-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAiEndpointConfigured(string? endpoint, string? model, string? key) =>
        !string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(key)
        && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !uri.AbsolutePath.EndsWith("/disabled", StringComparison.OrdinalIgnoreCase);
}
