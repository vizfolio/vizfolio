namespace Vizfolio.Application.Portfolios;

/// <summary>Spacing of the value-over-period series, chosen from the length of the period.</summary>
public enum PerformanceSeriesInterval
{
    Weekly,
    Monthly,
    Quarterly,
}

/// <summary>
/// One point on the value / returns-over-time charts: the balance at the close of <see cref="Date"/> (null
/// when some holding couldn't be valued — drawn as a gap, never guessed), the contributions that flowed in
/// and out since the previous point, the cumulative return from the period's start to this point, and the
/// cumulative investment gain (value change not explained by contributions).
/// </summary>
public sealed record PerformanceSeriesPoint(
    DateOnly Date,
    decimal? Value,
    decimal Deposits,
    decimal Withdrawals,
    decimal? CumulativeReturn,
    decimal? InvestmentGain);

public sealed record PerformanceSeriesResult(PerformanceSeriesInterval Interval, IReadOnlyList<PerformanceSeriesPoint> Points)
{
    public static PerformanceSeriesResult Empty { get; } = new(PerformanceSeriesInterval.Monthly, []);
}

/// <summary>
/// Builds the value / returns-over-time series. The first point is the period's opening — the close of the day
/// before <c>from</c>, i.e. the starting balance — then one point at the end of each week, month or
/// quarter (the last clamped to <c>to</c>, so it equals the ending balance). Each point's deposits and
/// withdrawals are the cash flows in <c>(previous point, point]</c>, so they sum to the period's
/// contributions. Valuation is supplied by the caller so the chart uses exactly the balances' rules.
/// <para>
/// Each point also carries its <see cref="PerformanceSeriesPoint.InvestmentGain"/> — value minus the opening
/// balance minus the net contributions to date — and its <see cref="PerformanceSeriesPoint.CumulativeReturn"/>,
/// supplied by <c>cumulativeReturnAt</c> so the caller can use the same return strategy as the headline figure.
/// Both are null when the point or the opening couldn't be fully valued; at the opening both are zero.
/// </para>
/// </summary>
public static class PerformanceSeriesBuilder
{
    private const int WeeklyUpToDays = 92;
    private const int MonthlyUpToYears = 10;

    public static PerformanceSeriesResult Build(
        DateOnly from,
        DateOnly to,
        Func<DateOnly, PerformanceBalanceResult> balanceAt,
        IReadOnlyList<CashFlow> cashFlows,
        Func<DateOnly, PerformanceBalanceResult, decimal?>? cumulativeReturnAt = null)
    {
        ArgumentNullException.ThrowIfNull(balanceAt);
        ArgumentNullException.ThrowIfNull(cashFlows);

        var interval = IntervalFor(from, to);
        var openingDate = from.AddDays(-1);
        var opening = balanceAt(openingDate);
        decimal? openingZero = opening.IsComplete ? 0m : null;
        var points = new List<PerformanceSeriesPoint>
        {
            new(openingDate, ValueOf(opening), 0m, 0m, openingZero, openingZero),
        };

        var previous = openingDate;
        var netContributed = 0m;
        foreach (var date in PointDates(from, to, interval))
        {
            var flows = cashFlows.Where(f => f.Date > previous && f.Date <= date).ToList();
            netContributed += flows.Sum(f => f.Amount);
            var balance = balanceAt(date);
            var bothComplete = opening.IsComplete && balance.IsComplete;

            points.Add(new PerformanceSeriesPoint(
                date,
                ValueOf(balance),
                flows.Where(f => f.Amount > 0m).Sum(f => f.Amount),
                flows.Where(f => f.Amount < 0m).Sum(f => f.Amount),
                bothComplete ? cumulativeReturnAt?.Invoke(date, balance) : null,
                bothComplete ? balance.Value - opening.Value - netContributed : null));
            previous = date;
        }

        return new PerformanceSeriesResult(interval, points);
    }

    public static PerformanceSeriesInterval IntervalFor(DateOnly from, DateOnly to)
    {
        if (to.DayNumber - from.DayNumber <= WeeklyUpToDays) return PerformanceSeriesInterval.Weekly;
        return to < from.AddYears(MonthlyUpToYears)
            ? PerformanceSeriesInterval.Monthly
            : PerformanceSeriesInterval.Quarterly;
    }

    /// <summary>End of each interval from <paramref name="from"/> onward; the last is clamped to <paramref name="to"/>.</summary>
    private static IEnumerable<DateOnly> PointDates(DateOnly from, DateOnly to, PerformanceSeriesInterval interval)
    {
        var end = FirstEnd(from, interval);
        while (end < to)
        {
            yield return end;
            end = NextEnd(end, interval);
        }

        yield return to;
    }

    private static DateOnly FirstEnd(DateOnly from, PerformanceSeriesInterval interval) => interval switch
    {
        PerformanceSeriesInterval.Weekly => from.AddDays(6),
        PerformanceSeriesInterval.Monthly => EndOfMonth(from),
        _ => EndOfQuarter(from),
    };

    private static DateOnly NextEnd(DateOnly end, PerformanceSeriesInterval interval) => interval switch
    {
        PerformanceSeriesInterval.Weekly => end.AddDays(7),
        PerformanceSeriesInterval.Monthly => EndOfMonth(end.AddDays(1)),
        _ => EndOfQuarter(end.AddDays(1)),
    };

    private static DateOnly EndOfMonth(DateOnly date)
        => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static DateOnly EndOfQuarter(DateOnly date)
    {
        var lastMonth = ((date.Month - 1) / 3 + 1) * 3;
        return new DateOnly(date.Year, lastMonth, DateTime.DaysInMonth(date.Year, lastMonth));
    }

    private static decimal? ValueOf(PerformanceBalanceResult balance) => balance.IsComplete ? balance.Value : null;
}
