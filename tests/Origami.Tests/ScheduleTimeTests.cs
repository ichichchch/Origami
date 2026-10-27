namespace Origami.Tests;

/// <summary>
/// 排程選擇器以鐘面時間運作，因此儲存的時間點由訪客自己的時區
/// 及其夏令時間變化決定。
/// </summary>
public sealed class ScheduleTimeTests
{
    [Fact]
    public void A_picked_wall_clock_keeps_its_time_across_a_daylight_saving_change()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        // 瀏覽器對整個工作階段只回報一個位移：夏令時間期間為 UTC-4。
        var browserOffset = TimeSpan.FromHours(-4);
        var before = new DateTimeOffset(2026, 10, 26, 10, 0, 0, browserOffset);
        var after = new DateTimeOffset(2026, 11, 2, 10, 0, 0, browserOffset);

        var beforeUtc = ScheduleTime.ToUtc(before, zone);
        var afterUtc = ScheduleTime.ToUtc(after, zone);

        Assert.Equal(new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero), beforeUtc);
        // 2026-11-01 紐約結束夏令時間，所以 10:00 是 UTC-5 而非 UTC-4。
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero), afterUtc);
        // 鐘面時間相差七天，但時區在中間多出了一小時。
        Assert.Equal(TimeSpan.FromDays(7) + TimeSpan.FromHours(1), afterUtc - beforeUtc);
    }

    [Fact]
    public void A_wall_clock_time_that_does_not_exist_is_refused()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        // 2026-03-08 02:30 在紐約不存在：時鐘從 02:00 直接跳到 03:00。
        var picked = new DateTimeOffset(2026, 3, 8, 2, 30, 0, TimeSpan.FromHours(-5));
        var refused = Assert.Throws<CmsException>(() => ScheduleTime.ToUtc(picked, zone));
        Assert.Contains("不存在", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stored_instant_comes_back_as_the_same_wall_clock()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var utc = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
        var local = ScheduleTime.ToLocal(utc, zone);
        Assert.Equal(new DateTime(2026, 11, 2, 10, 0, 0), local.DateTime);
        Assert.Equal(utc, ScheduleTime.ToUtc(local, zone));
    }

    [Fact]
    public void An_unknown_zone_falls_back_to_the_browser_offset()
    {
        var zone = ScheduleTime.Resolve("Not/AZone", TimeSpan.FromMinutes(330));
        Assert.Equal(TimeSpan.FromMinutes(330), zone.GetUtcOffset(new DateTime(2026, 10, 26, 10, 0, 0)));
        var utc = ScheduleTime.ToUtc(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromMinutes(330)), zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 4, 30, 0, TimeSpan.Zero), utc);
    }
}
