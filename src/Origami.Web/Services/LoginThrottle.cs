namespace Origami.Web.Services;

/// <summary>依連線來源限制所有憑證嘗試次數，不論登入結果為何。</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxAttempts = 20;
    public const int MaxSources = 512;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> attempts = new(StringComparer.Ordinal);
    // 每個來源恰保留一筆到期記錄；清理時只走訪已有過期嘗試的來源。
    private readonly PriorityQueue<string, DateTimeOffset> expirations = new();

    /// <summary>在驗證憑證前以原子方式消耗一個許可。缺少來源資訊者共用同一份預算。</summary>
    public bool TryAcquire(string? source)
    {
        source = string.IsNullOrEmpty(source) ? "unknown" : source;
        lock (gate)
        {
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            if (!attempts.TryGetValue(source, out var queue))
            {
                // 容量滿時保守拒絕新來源，直到有記錄到期；活躍記錄絕不逐出。
                if (attempts.Count >= MaxSources) return false;
                queue = new Queue<DateTimeOffset>(MaxAttempts);
                attempts.Add(source, queue);
                expirations.Enqueue(source, now + Window);
            }
            if (queue.Count >= MaxAttempts) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        while (expirations.TryPeek(out var source, out var expiresAt) && expiresAt <= now)
        {
            expirations.Dequeue();
            var queue = attempts[source];
            while (queue.TryPeek(out var attemptedAt) && attemptedAt + Window <= now)
                queue.Dequeue();
            if (queue.Count == 0) attempts.Remove(source);
            else expirations.Enqueue(source, queue.Peek() + Window);
        }
    }
}
