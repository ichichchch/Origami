using System.Reflection;
using Origami.Web.Services;

namespace Origami.Tests;

public sealed class LoginThrottleTests
{
    [Fact]
    public void Attempts_expire_at_the_exact_window_boundary_without_rejections_extending_it()
    {
        var clock = new ManualTimeProvider();
        var throttle = new LoginThrottle(clock);
        Assert.True(throttle.TryAcquire("192.0.2.1"));
        clock.Advance(TimeSpan.FromTicks(1));
        for (var attempt = 1; attempt < LoginThrottle.MaxAttempts; attempt++)
            Assert.True(throttle.TryAcquire("192.0.2.1"));
        Assert.False(throttle.TryAcquire("192.0.2.1"));

        clock.Advance(LoginThrottle.Window - TimeSpan.FromTicks(2));
        Assert.False(throttle.TryAcquire("192.0.2.1"));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(throttle.TryAcquire("192.0.2.1"));
        Assert.False(throttle.TryAcquire("192.0.2.1"));
        AssertStorage(throttle, 1, LoginThrottle.MaxAttempts);

        clock.Advance(TimeSpan.FromTicks(1));
        for (var attempt = 1; attempt < LoginThrottle.MaxAttempts; attempt++)
            Assert.True(throttle.TryAcquire("192.0.2.1"));
        Assert.False(throttle.TryAcquire("192.0.2.1"));
        AssertStorage(throttle, 1, LoginThrottle.MaxAttempts);
    }

    [Fact]
    public void Sources_have_independent_budgets()
    {
        var throttle = new LoginThrottle(new ManualTimeProvider());
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts; attempt++)
            Assert.True(throttle.TryAcquire("192.0.2.1"));
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts; attempt++)
        {
            Assert.False(throttle.TryAcquire("192.0.2.1"));
            Assert.True(throttle.TryAcquire("192.0.2.2"));
        }
        Assert.False(throttle.TryAcquire("192.0.2.2"));
        AssertStorage(throttle, 2, 2 * LoginThrottle.MaxAttempts);
    }

    [Fact]
    public void Missing_sources_share_the_unknown_budget_instead_of_bypassing_it()
    {
        var throttle = new LoginThrottle(new ManualTimeProvider());
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts; attempt++)
            Assert.True(throttle.TryAcquire(attempt % 2 == 0 ? null : string.Empty));
        Assert.False(throttle.TryAcquire(null));
        Assert.False(throttle.TryAcquire(string.Empty));
        Assert.False(throttle.TryAcquire("unknown"));
        AssertStorage(throttle, 1, LoginThrottle.MaxAttempts);
    }

    [Fact]
    public void Concurrent_acquisitions_cannot_exceed_the_source_limit()
    {
        var throttle = new LoginThrottle(new ManualTimeProvider());
        var acquired = 0;
        Parallel.For(0, 2_000, _ =>
        {
            if (throttle.TryAcquire("192.0.2.1")) Interlocked.Increment(ref acquired);
        });
        Assert.Equal(LoginThrottle.MaxAttempts, acquired);
        AssertStorage(throttle, 1, LoginThrottle.MaxAttempts);
    }

    [Fact]
    public void Concurrent_new_sources_cannot_exceed_storage_capacity()
    {
        var throttle = new LoginThrottle(new ManualTimeProvider());
        var acquired = 0;
        Parallel.For(0, LoginThrottle.MaxSources * 4, source =>
        {
            if (throttle.TryAcquire($"2001:db8::{source:x}")) Interlocked.Increment(ref acquired);
        });
        Assert.Equal(LoginThrottle.MaxSources, acquired);
        AssertStorage(throttle, LoginThrottle.MaxSources, LoginThrottle.MaxSources);
    }

    [Fact]
    public void Storage_and_expiry_bookkeeping_stay_bounded_during_rejections_and_source_churn()
    {
        Assert.Equal(512, LoginThrottle.MaxSources);
        Assert.Equal(20, LoginThrottle.MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), LoginThrottle.Window);
        var clock = new ManualTimeProvider();
        var throttle = new LoginThrottle(clock);
        for (var round = 0; round < 3; round++)
        {
            for (var source = 0; source < LoginThrottle.MaxSources; source++)
            {
                var address = $"2001:db8:{round:x}::{source:x}";
                for (var attempt = 0; attempt < LoginThrottle.MaxAttempts * 2; attempt++)
                    Assert.Equal(attempt < LoginThrottle.MaxAttempts, throttle.TryAcquire(address));
                AssertStorage(throttle, source + 1, (source + 1) * LoginThrottle.MaxAttempts);
            }
            for (var source = LoginThrottle.MaxSources; source < LoginThrottle.MaxSources * 4; source++)
            {
                Assert.False(throttle.TryAcquire($"2001:db8:{round:x}::{source:x}"));
                AssertStorage(throttle, LoginThrottle.MaxSources, LoginThrottle.MaxSources * LoginThrottle.MaxAttempts);
            }
            Assert.False(throttle.TryAcquire($"2001:db8:{round:x}::0"));
            clock.Advance(LoginThrottle.Window);
        }
    }

    [Fact]
    public void Saturation_fails_closed_until_expiry_without_evicting_live_sources()
    {
        var clock = new ManualTimeProvider();
        var throttle = new LoginThrottle(clock);
        const string liveSource = "2001:db8::0";
        for (var source = 0; source < LoginThrottle.MaxSources; source++)
            Assert.True(throttle.TryAcquire($"2001:db8::{source:x}"));
        Assert.False(throttle.TryAcquire("198.51.100.1"));

        clock.Advance(TimeSpan.FromMinutes(1));
        for (var attempt = 1; attempt < LoginThrottle.MaxAttempts; attempt++)
            Assert.True(throttle.TryAcquire(liveSource));
        Assert.False(throttle.TryAcquire(liveSource));
        Assert.False(throttle.TryAcquire("198.51.100.1"));
        clock.Advance(LoginThrottle.Window - TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1));
        Assert.False(throttle.TryAcquire("198.51.100.1"));
        Assert.False(throttle.TryAcquire(liveSource));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(throttle.TryAcquire("198.51.100.1"));
        Assert.True(throttle.TryAcquire(liveSource));
        Assert.False(throttle.TryAcquire(liveSource));
        AssertStorage(throttle, 2, LoginThrottle.MaxAttempts + 1);
    }

    private static void AssertStorage(LoginThrottle throttle, int expectedSources, int expectedAttempts)
    {
        // Inspect only while acquisitions are stopped, without adding test-only production APIs.
        var attempts = Assert.IsType<Dictionary<string, Queue<DateTimeOffset>>>(typeof(LoginThrottle)
            .GetField("attempts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(throttle));
        var expirations = Assert.IsType<PriorityQueue<string, DateTimeOffset>>(typeof(LoginThrottle)
            .GetField("expirations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(throttle));
        Assert.Equal(expectedSources, attempts.Count);
        Assert.InRange(attempts.Count, 0, LoginThrottle.MaxSources);
        Assert.All(attempts.Values, queue => Assert.InRange(queue.Count, 1, LoginThrottle.MaxAttempts));
        Assert.Equal(expectedAttempts, attempts.Values.Sum(queue => queue.Count));
        Assert.Equal(attempts.Count, expirations.Count);
        Assert.Equal(attempts.Count, expirations.UnorderedItems.Select(item => item.Element).Distinct().Count());
        Assert.All(expirations.UnorderedItems, item =>
            Assert.Equal(attempts[item.Element].Peek() + LoginThrottle.Window, item.Priority));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}
