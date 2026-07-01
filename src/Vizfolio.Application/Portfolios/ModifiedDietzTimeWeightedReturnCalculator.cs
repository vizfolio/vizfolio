namespace Vizfolio.Application.Portfolios;

// R = (EMV - BMV - sum(Ci)) / (BMV + sum(wi * Ci))
// wi = (T - ti) / T  — fraction of the period remaining after flow i.
// Widely used as a period-return TWRR proxy when only start/end valuations are available.
// Formally a money-weighted approximation; label the response Method as ModifiedDietz so consumers can tell.
public sealed class ModifiedDietzTimeWeightedReturnCalculator : ITimeWeightedReturnCalculator
{
    private const string MethodName = "ModifiedDietz";
    private const string BasisName = "Period";

    public ReturnResult Compute(PerformanceComputationContext ctx)
    {
        if (!ctx.StartingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteStartingBalance");
        if (!ctx.EndingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteEndingBalance");

        var totalDays = ctx.To.DayNumber - ctx.From.DayNumber;
        if (totalDays <= 0)
            return new ReturnResult(null, MethodName, BasisName, "PeriodTooShort");

        decimal netContribution = 0m;
        decimal weightedContribution = 0m;
        foreach (var cf in ctx.CashFlows)
        {
            netContribution += cf.Amount;
            var dayFromStart = cf.Date.DayNumber - ctx.From.DayNumber;
            if (dayFromStart < 0) dayFromStart = 0;
            if (dayFromStart > totalDays) dayFromStart = totalDays;
            var weight = (decimal)(totalDays - dayFromStart) / totalDays;
            weightedContribution += weight * cf.Amount;
        }

        var denominator = ctx.StartingBalance + weightedContribution;
        if (denominator == 0m)
            return new ReturnResult(null, MethodName, BasisName, "ZeroDenominator");

        var rate = (ctx.EndingBalance - ctx.StartingBalance - netContribution) / denominator;
        return new ReturnResult(rate, MethodName, BasisName, null);
    }
}
