namespace Origami.Core.Services;

/// <summary>受控擴充點（僅原始碼層）：核心事件訂閱者由部署方以 DI 註冊；
/// 核心絕不載入外部組件或執行外部程式碼，也不提供線上市場。</summary>
public interface ICmsEventSubscriber
{
    /// <summary>處理核心事件。同步執行；拋出的例外會被記錄，不影響主流程。</summary>
    Task HandleAsync(CmsEvent @event, CancellationToken ct = default);
}

/// <summary>核心事件：名稱與出站 Webhook 對齊（content.published、comment.created…）；
/// 資料為扁平字串字典，僅供通知參考，不是完整內容快照。</summary>
public sealed record CmsEvent(string Name, Guid SiteId, DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string?> Data);

/// <summary>事件匯流排：分發核心事件給已註冊的訂閱者；沒有訂閱者時零成本。</summary>
public sealed class CmsEventBus(IEnumerable<ICmsEventSubscriber> subscribers, ILogger<CmsEventBus>? logger = null)
{
    public async Task PublishAsync(string name, Guid siteId, object payload, CancellationToken ct = default)
    {
        var list = subscribers as IReadOnlyList<ICmsEventSubscriber> ?? [.. subscribers];
        if (list.Count == 0) return;
        var @event = new CmsEvent(name, siteId, TimeProvider.System.GetUtcNow(), Flatten(payload));
        foreach (var subscriber in list)
        {
            try { await subscriber.HandleAsync(@event, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger?.LogError(ex, "CMS 事件訂閱者處理失敗：{EventName}", name); }
        }
    }

    /// <summary>把匿名事件資料攤平成字串字典；巢狀物件以 JSON 原文呈現。</summary>
    private static IReadOnlyDictionary<string, string?> Flatten(object payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            });
    }
}
