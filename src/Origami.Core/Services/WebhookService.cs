using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Origami.Core.Services;

/// <summary>
/// 出站 Webhook 調度器：當內容發布或新評論建立且站點設定了 WebhookUrl 時，
/// 以結構化 JSON POST 通知外部端點，並附帶 X-Origami-Signature（HMAC-SHA256）。
/// 調度採盡力而為（best-effort），外部端點失敗或逾時絕不阻斷內部交易。
/// </summary>
public sealed class WebhookService(
    IDbContextFactory<CmsDbContext> factory,
    IHttpClientFactory? httpClients = null,
    TimeProvider? clock = null,
    ILogger<WebhookService>? logger = null)
{
    public sealed record WebhookDelivery(string Event, Guid SiteId, DateTimeOffset Timestamp, object Data, string? Signature);

    private readonly List<WebhookDelivery> _deliveries = [];
    private readonly Lock _lock = new();

    /// <summary>最近觸發的出站事件快照（供測試與診斷檢視）。</summary>
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

            if (httpClients is not null)
            {
                using var client = httpClients.CreateClient("OrigamiWebhook");
                client.Timeout = TimeSpan.FromSeconds(5);
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Origami-Event", eventName);
                if (signature is not null)
                    request.Headers.Add("X-Origami-Signature", signature);
                using var _ = await client.SendAsync(request, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Webhook dispatch failed for event {Event}", eventName);
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
