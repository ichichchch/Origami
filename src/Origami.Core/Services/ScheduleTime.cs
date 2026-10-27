namespace Origami.Core.Services;

/// <summary>
/// 在排程選擇器的當地時鐘時間與 UTC 之間轉換。偏移量由訪客自身的時區決定，
/// 因此日期落在夏令時間變化的另一側時，
/// 仍保有當初選定的當地時鐘時間。
/// </summary>
public static class ScheduleTime
{
    /// <summary>給定時區內所選當地時鐘時間對應的 UTC 時刻。</summary>
    public static DateTimeOffset ToUtc(DateTimeOffset picked, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(picked.DateTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallClock))
            throw new CmsException("此排程時間在您所在的時區不存在，請改選其他時間。");
        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock)).ToUniversalTime();
    }

    /// <summary>給定時區內 UTC 時刻對應的當地時鐘時間。</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone);

    /// <summary>
    /// 訪客的時區。具名時區保有夏令時間規則；未知名稱
    /// 則退回瀏覽器回報的固定偏移。
    /// </summary>
    public static TimeZoneInfo Resolve(string? timeZoneId, TimeSpan fallbackOffset)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // 此機器不認得這個時區名稱；以瀏覽器偏移作為後備。
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Origami.BrowserOffset", fallbackOffset, "Browser offset", "Browser offset");
    }
}
