using System.Collections.Concurrent;

namespace WordPressNet.Web.Services;

/// <summary>
/// Limits repeated failed sign-in attempts per client. The Blazor sign-in page runs as a
/// component rather than an endpoint, so endpoint metadata cannot carry this limit.
/// </summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaxAttempts = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> failures = new();

    /// <summary>Reports whether the client exceeded the allowed number of failed attempts.</summary>
    public bool IsBlocked(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!failures.TryGetValue(key, out var queue)) return false;
        lock (queue)
        {
            RemoveExpired(queue, clock.GetUtcNow());
            if (queue.Count == 0) failures.TryRemove(key, out _);
            return queue.Count >= MaxAttempts;
        }
    }

    /// <summary>Records one failed attempt.</summary>
    public void RegisterFailure(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        var now = clock.GetUtcNow();
        var queue = failures.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            RemoveExpired(queue, now);
            queue.Enqueue(now);
        }
        if (failures.Count > 512) RemoveIdleEntries(now);
    }

    /// <summary>Clears the recorded failures after a successful sign-in.</summary>
    public void Reset(string key)
    {
        if (!string.IsNullOrEmpty(key)) failures.TryRemove(key, out _);
    }

    private static void RemoveExpired(Queue<DateTimeOffset> queue, DateTimeOffset now)
    {
        while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();
    }

    private void RemoveIdleEntries(DateTimeOffset now)
    {
        foreach (var entry in failures)
        {
            lock (entry.Value)
            {
                RemoveExpired(entry.Value, now);
                if (entry.Value.Count == 0) failures.TryRemove(entry.Key, out _);
            }
        }
    }
}
