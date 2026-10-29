namespace Origami.Web.Services;

/// <summary>依連線來源限制所有憑證嘗試次數，不論登入結果為何。</summary>
public sealed class LoginThrottle(TimeProvider clock,
    int? maxAttempts = null, int? maxSources = null, TimeSpan? window = null)
{
    public const int MaxAttempts = 20;
    public const int MaxSources = 512;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly int attemptLimit = maxAttempts ?? MaxAttempts;
    private readonly int sourceLimit = maxSources ?? MaxSources;
    private readonly TimeSpan windowDuration = window ?? Window;

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
                if (attempts.Count >= sourceLimit) return false;
                queue = new Queue<DateTimeOffset>(attemptLimit);
                attempts.Add(source, queue);
                expirations.Enqueue(source, now + windowDuration);
            }
            if (queue.Count >= attemptLimit) return false;
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
            while (queue.TryPeek(out var attemptedAt) && attemptedAt + windowDuration <= now)
                queue.Dequeue();
            if (queue.Count == 0) attempts.Remove(source);
            else expirations.Enqueue(source, queue.Peek() + windowDuration);
        }
    }
}

/// <summary>
/// 內容保護密碼的解鎖嘗試預算；與登入限流分開計數，避免解鎖流量排擠登入來源。
/// Api 端解鎖走 BFF 免限流通道，線上猜測的失敗上限由網站主機這層補上。
/// </summary>
public sealed class ContentUnlockThrottle(TimeProvider clock)
{
    public const int MaxAttempts = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly LoginThrottle budget = new(clock, MaxAttempts, maxSources: 4096, Window);

    /// <summary>以「內容識別＋來源 IP」為鍵消耗一個解鎖許可。</summary>
    public bool TryAcquire(Guid contentId, string? remoteIp) =>
        budget.TryAcquire(contentId.ToString("N") + ":" + (remoteIp ?? "unknown"));
}

/// <summary>
/// 訪客評論的張貼預算；與登入限流分開計數。過量時提示稍後再試，不封鎖閱讀。
/// </summary>
public sealed class CommentThrottle(TimeProvider clock)
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly LoginThrottle budget = new(clock, MaxAttempts, maxSources: 4096, Window);

    /// <summary>以來源 IP 為鍵消耗一個張貼許可。</summary>
    public bool TryAcquire(string? remoteIp) => budget.TryAcquire("comment:" + (remoteIp ?? "unknown"));
}
