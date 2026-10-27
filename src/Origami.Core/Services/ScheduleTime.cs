namespace Origami.Core.Services;

/// <summary>
/// Converts between the wall-clock time of a scheduling picker and UTC. The visitor's own time
/// zone decides the offset, so a date on the other side of a daylight-saving change keeps the
/// wall-clock time that was picked.
/// </summary>
public static class ScheduleTime
{
    /// <summary>The UTC instant of a picked wall-clock time inside the given zone.</summary>
    public static DateTimeOffset ToUtc(DateTimeOffset picked, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(picked.DateTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallClock))
            throw new CmsException("此排程時間在您所在的時區不存在，請改選其他時間。");
        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock)).ToUniversalTime();
    }

    /// <summary>The wall-clock time of a UTC instant inside the given zone.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone);

    /// <summary>
    /// The visitor's time zone. A named zone keeps its daylight-saving rules; an unknown name
    /// falls back to the fixed offset the browser reported.
    /// </summary>
    public static TimeZoneInfo Resolve(string? timeZoneId, TimeSpan fallbackOffset)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // The zone name is unknown on this machine; the browser offset is the fallback.
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Origami.BrowserOffset", fallbackOffset, "Browser offset", "Browser offset");
    }
}
