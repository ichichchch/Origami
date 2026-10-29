using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Origami.Core.Services;

/// <summary>
/// 出站 Webhook 調度器：當內容發布或新評論建立且站點設定了 WebhookUrl 時，
/// 以結構化 JSON POST 通知外部端點，並附帶 X-Origami-Signature（HMAC-SHA256）。
/// 投遞紀錄持久化於資料庫，支援指數退避自動重試（Pending / RetryScheduled）與後台人工重送。
/// </summary>
public sealed class WebhookService(
    IDbContextFactory<CmsDbContext> factory,
    IHttpClientFactory? httpClients = null,
    TimeProvider? clock = null,
    ILogger<WebhookService>? logger = null,
    AdminAccess? admin = null)
{
    public sealed record WebhookDelivery(string Event, Guid SiteId, DateTimeOffset Timestamp, object Data, string? Signature);

    public sealed record WebhookDeliveryView(
        Guid Id,
        string Event,
        string TargetUrl,
        WebhookDeliveryStatus Status,
        int Attempts,
        int? LastStatusCode,
        string LastError,
        string Signature,
        DateTimeOffset CreatedAt,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? DeliveredAt);

    private readonly List<WebhookDelivery> _deliveries = [];
    private readonly Lock _lock = new();

    /// <summary>最近觸發的出站事件快照（供測試與即時診斷檢視）。</summary>
    public IReadOnlyList<WebhookDelivery> RecentDeliveries
    {
        get { lock (_lock) return [.. _deliveries]; }
    }

    public async Task DispatchAsync(string eventName, object payload, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (settings is null || string.IsNullOrWhiteSpace(settings.WebhookUrl)) return;
            var url = settings.WebhookUrl.Trim();
            if (!IsAllowedWebhookUrl(url)) return;

            var now = (clock ?? TimeProvider.System).GetUtcNow();
            var envelope = new
            {
                @event = eventName,
                siteId = db.CurrentSiteId,
                timestamp = now,
                data = payload
            };
            var json = JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web);
            if (json.Length > Limits.WebhookPayloadMax)
                json = json[..Limits.WebhookPayloadMax];

            string? signature = null;
            if (!string.IsNullOrWhiteSpace(settings.WebhookSecret))
            {
                var key = Encoding.UTF8.GetBytes(settings.WebhookSecret.Trim());
                var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
                signature = "sha256=" + Convert.ToHexStringLower(hash);
            }

            lock (_lock)
            {
                _deliveries.Add(new WebhookDelivery(eventName, db.CurrentSiteId, now, payload, signature));
                if (_deliveries.Count > 50) _deliveries.RemoveAt(0);
            }

            var record = new WebhookDeliveryRecord
            {
                SiteId = db.CurrentSiteId,
                Event = eventName,
                TargetUrl = url,
                PayloadJson = json,
                Signature = signature ?? "",
                Status = WebhookDeliveryStatus.Pending,
                Attempts = 0,
                CreatedAt = now,
                NextAttemptAt = now
            };
            db.WebhookDeliveries.Add(record);
            await db.SaveChangesAsync(ct);

            await AttemptSendAsync(db, record, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Webhook dispatch failed for event {Event}", eventName);
        }
    }

    /// <summary>後台查詢持久化 Webhook 投遞紀錄。</summary>
    public async Task<PagedResult<WebhookDeliveryView>> ListAdminAsync(int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        if (admin is not null) await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.WebhookDeliveries.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize)
            .Select(x => new WebhookDeliveryView(
                x.Id, x.Event, x.TargetUrl, x.Status, x.Attempts, x.LastStatusCode,
                x.LastError, x.Signature, x.CreatedAt, x.NextAttemptAt, x.DeliveredAt))
            .ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    /// <summary>後台人工重送單筆 Webhook 投遞。</summary>
    public async Task<WebhookDeliveryView> RetryManualAsync(Guid id, CancellationToken ct = default)
    {
        if (admin is not null) await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var record = await db.WebhookDeliveries.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CmsException("Webhook 投遞紀錄不存在。");
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await AttemptSendAsync(db, record, now, ct);
        return new WebhookDeliveryView(
            record.Id, record.Event, record.TargetUrl, record.Status, record.Attempts,
            record.LastStatusCode, record.LastError, record.Signature, record.CreatedAt,
            record.NextAttemptAt, record.DeliveredAt);
    }

    /// <summary>背景輪詢：處理到期需重試的 Webhook 投遞。</summary>
    public async Task<int> ProcessPendingRetriesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var due = await db.WebhookDeliveries
            .Where(x => (x.Status == WebhookDeliveryStatus.Pending || x.Status == WebhookDeliveryStatus.RetryScheduled)
                && x.NextAttemptAt != null && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Id)
            .Take(20)
            .ToListAsync(ct);
        var processed = 0;
        foreach (var record in due)
        {
            await AttemptSendAsync(db, record, now, ct);
            processed++;
        }
        return processed;
    }

    private async Task AttemptSendAsync(CmsDbContext db, WebhookDeliveryRecord record, DateTimeOffset now, CancellationToken ct)
    {
        record.Attempts++;
        if (!IsAllowedWebhookUrl(record.TargetUrl))
        {
            record.Status = WebhookDeliveryStatus.Failed;
            record.LastError = "目標網址不符合安全規則。";
            record.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        if (httpClients is null)
        {
            record.Status = WebhookDeliveryStatus.Succeeded;
            record.LastStatusCode = 200;
            record.LastError = "";
            record.DeliveredAt = now;
            record.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            using var client = httpClients.CreateClient("OrigamiWebhook");
            client.Timeout = TimeSpan.FromSeconds(5);
            using var request = new HttpRequestMessage(HttpMethod.Post, record.TargetUrl)
            {
                Content = new StringContent(record.PayloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Origami-Event", record.Event);
            request.Headers.Add("X-Origami-Delivery", record.Id.ToString("D"));
            if (!string.IsNullOrEmpty(record.Signature))
                request.Headers.Add("X-Origami-Signature", record.Signature);

            using var response = await client.SendAsync(request, ct);
            record.LastStatusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                record.Status = WebhookDeliveryStatus.Succeeded;
                record.LastError = "";
                record.DeliveredAt = now;
                record.NextAttemptAt = null;
            }
            else
            {
                ScheduleRetryOrFail(record, now, $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var errMsg = ex.GetType().Name;
            ScheduleRetryOrFail(record, now, errMsg);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void ScheduleRetryOrFail(WebhookDeliveryRecord record, DateTimeOffset now, string errorSummary)
    {
        record.LastError = errorSummary.Length <= Limits.WebhookErrorMax ? errorSummary : errorSummary[..Limits.WebhookErrorMax];
        if (record.Attempts >= Limits.WebhookMaxAttempts)
        {
            record.Status = WebhookDeliveryStatus.Failed;
            record.NextAttemptAt = null;
        }
        else
        {
            record.Status = WebhookDeliveryStatus.RetryScheduled;
            // 指數退避：1m, 2m, 4m, 8m...
            var delayMinutes = Math.Pow(2, Math.Max(0, record.Attempts - 1));
            record.NextAttemptAt = now.AddMinutes(delayMinutes);
        }
    }

    public static bool IsAllowedWebhookUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not "https" || uri.Host.Length == 0) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
                return false;
            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] is 10 or 127 or 0) return false;
                if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return false;
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                if (bytes[0] == 169 && bytes[1] == 254) return false;
                if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return false;
            }
            else if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (bytes[0] & 0xfe) == 0xfc)
            {
                return false;
            }
        }
        return true;
    }
}
