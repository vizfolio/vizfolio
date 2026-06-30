namespace Vizfolio.Application.Performance.RateOfReturn;

public static class TimeWeightedReturnCalculator
{
    /// <param name="valuationAt">Returns the portfolio value at end-of-day for a given date,
    /// carried forward from the latest known snapshot if no snapshot exists on that date.</param>
    public static decimal? Calculate(
        decimal beginningBalance,
        decimal endingBalance,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<CashFlow> flows,
        Func<DateOnly, decimal> valuationAt)
    {
        if (beginningBalance == 0m)
            return null;

        // Group flows by date — multiple flows on the same date are summed and treated as one.
        var flowsByDate = flows
            .Where(f => f.Date > periodStart && f.Date <= periodEnd)
            .GroupBy(f => f.Date)
            .OrderBy(g => g.Key)
            .Select(g => (Date: g.Key, Amount: g.Sum(f => f.Amount)))
            .ToList();

        decimal product = 1m;
        decimal prevValue = beginningBalance;

        foreach (var (date, amount) in flowsByDate)
        {
            // Sub-period return ends just before the flow — use the prior end-of-day valuation.
            var valueBeforeFlow = valuationAt(date.AddDays(-1));
            if (prevValue == 0m)
                return null;

            var subReturn = (valueBeforeFlow - prevValue) / prevValue;
            product *= 1m + subReturn;

            // Post-flow value re-bases the next sub-period.
            prevValue = valueBeforeFlow + amount;
        }

        if (prevValue == 0m)
            return null;

        var finalReturn = (endingBalance - prevValue) / prevValue;
        product *= 1m + finalReturn;

        return product - 1m;
    }
}
