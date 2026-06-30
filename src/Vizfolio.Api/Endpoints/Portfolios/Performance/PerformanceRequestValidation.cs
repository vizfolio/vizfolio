using Vizfolio.Application.Performance;

namespace Vizfolio.Api.Endpoints.Portfolios.Performance;

internal static class PerformanceRequestValidation
{
    public const int MaxDailyRangeDays = 366;

    public static (DateOnly From, DateOnly To, PerformanceGranularity Granularity)? Resolve(
        DateOnly? from,
        DateOnly? to,
        PerformanceGranularity? granularity,
        out string? error)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var toResolved = to ?? today;
        var fromResolved = from ?? toResolved.AddYears(-1);
        var granResolved = granularity ?? PerformanceGranularity.Monthly;

        if (fromResolved > toResolved)
        {
            error = "'from' must be on or before 'to'.";
            return null;
        }

        if (toResolved > today)
        {
            error = "'to' cannot be in the future.";
            return null;
        }

        if (granResolved == PerformanceGranularity.Daily
            && toResolved.DayNumber - fromResolved.DayNumber + 1 > MaxDailyRangeDays)
        {
            error = $"Daily granularity is limited to {MaxDailyRangeDays} days.";
            return null;
        }

        error = null;
        return (fromResolved, toResolved, granResolved);
    }
}
