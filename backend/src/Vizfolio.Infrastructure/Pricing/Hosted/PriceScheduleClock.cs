namespace Vizfolio.Infrastructure.Pricing.Hosted;

/// <summary>
/// The daily price-refresh time (<see cref="PriceSchedule.DailyAt"/> in <see cref="PriceSchedule.TimeZone"/> — after the
/// US close, once the day's closes are published), shared by the scheduler (when to run next) and the worker (since when
/// a fetched series can't have a newer close).
/// </summary>
internal static class PriceScheduleClock
{
    /// <summary>The next scheduled refresh after <paramref name="nowUtc"/>: today's or tomorrow's DailyAt, or now + Interval.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset nowUtc, PriceSchedule schedule, TimeZoneInfo zone)
    {
        if (schedule.DailyAt is not { } dailyAt) return nowUtc + Interval(schedule);

        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var candidate = local.Date + dailyAt;
        if (candidate <= local.DateTime) candidate = candidate.AddDays(1);
        return ToUtc(candidate, zone);
    }

    /// <summary>
    /// The latest daily refresh time at or before <paramref name="nowUtc"/> (or now − Interval): a series fetched since then
    /// already has every close a provider can have published.
    /// </summary>
    public static DateTimeOffset LatestClose(DateTimeOffset nowUtc, PriceSchedule schedule, TimeZoneInfo zone)
    {
        if (schedule.DailyAt is not { } dailyAt) return nowUtc - Interval(schedule);

        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var candidate = local.Date + dailyAt;
        if (candidate > local.DateTime) candidate = candidate.AddDays(-1);
        return ToUtc(candidate, zone);
    }

    public static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static TimeSpan Interval(PriceSchedule schedule)
        => schedule.Interval <= TimeSpan.Zero ? TimeSpan.FromHours(24) : schedule.Interval;

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
        => new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
}
