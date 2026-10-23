namespace Origami.Web.Services;

/// <summary>Bounds all credential attempts per connection source, regardless of sign-in outcome.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxAttempts = 20;
    public const int MaxSources = 512;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> attempts = new(StringComparer.Ordinal);
    // Exactly one expiry entry per source; requests visit only sources with expired attempts.
    private readonly PriorityQueue<string, DateTimeOffset> expirations = new();

    /// <summary>Atomically consumes a permit before checking credentials. Missing sources share one budget.</summary>
    public bool TryAcquire(string? source)
    {
        source = string.IsNullOrEmpty(source) ? "unknown" : source;
        lock (gate)
        {
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            if (!attempts.TryGetValue(source, out var queue))
            {
                // At capacity, new sources fail closed until an entry expires; live entries are never evicted.
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
