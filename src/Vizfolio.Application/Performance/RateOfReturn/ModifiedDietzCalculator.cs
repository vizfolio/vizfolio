namespace Vizfolio.Application.Performance.RateOfReturn;

public static class ModifiedDietzCalculator
{
    public static decimal? Calculate(
        decimal beginningBalance,
        decimal endingBalance,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<CashFlow> flows)
    {
        var totalDays = periodEnd.DayNumber - periodStart.DayNumber;
        if (totalDays <= 0)
            return null;

        decimal netFlow = 0m;
        decimal weightedFlow = 0m;
        foreach (var flow in flows)
        {
            var dayOffset = flow.Date.DayNumber - periodStart.DayNumber;
            // Clamp into [0, totalDays] so flows on the boundary still contribute.
            if (dayOffset < 0) dayOffset = 0;
            if (dayOffset > totalDays) dayOffset = totalDays;

            var weight = (decimal)(totalDays - dayOffset) / totalDays;
            netFlow += flow.Amount;
            weightedFlow += weight * flow.Amount;
        }

        var denominator = beginningBalance + weightedFlow;
        if (denominator == 0m)
            return null;

        return (endingBalance - beginningBalance - netFlow) / denominator;
    }
}
