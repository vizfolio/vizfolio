namespace Vizfolio.Application.Portfolios;

// Geometric chain of sub-period returns, with sub-period boundaries at each intermediate
// snapshot date. Each sub-period return is computed with Modified Dietz to handle
// mid-sub-period cash flows. Approaches GIPS-compliant TWRR as interim snapshot density
// increases; returns null when no interior snapshots are available.
public sealed class ChainedSubPeriodTimeWeightedReturnCalculator : ITimeWeightedReturnCalculator
{
    private const string MethodName = "ChainedSubPeriods";
    private const string BasisName = "Period";

    public ReturnResult Compute(PerformanceComputationContext ctx)
    {
        if (!ctx.StartingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteStartingBalance");
        if (!ctx.EndingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteEndingBalance");

        var interior = ctx.IntermediateBalances
            .Where(b => b.Date > ctx.From && b.Date < ctx.To)
            .OrderBy(b => b.Date)
            .ToList();

        if (interior.Count == 0)
            return new ReturnResult(null, MethodName, BasisName, "InsufficientIntermediateSnapshots");

        var boundaries = new List<BalancePoint>(interior.Count + 2)
        {
            new(ctx.From, ctx.StartingBalance),
        };
        boundaries.AddRange(interior);
        boundaries.Add(new BalancePoint(ctx.To, ctx.EndingBalance));

        decimal cumulative = 1m;
        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            var startPoint = boundaries[i];
            var endPoint = boundaries[i + 1];
            var subDays = endPoint.Date.DayNumber - startPoint.Date.DayNumber;
            if (subDays <= 0)
                return new ReturnResult(null, MethodName, BasisName, "InvalidSubPeriod");

            // The period opens at the start of `from` (the starting balance is the close of the day before), so
            // the first sub-period also owns flows dated `from`; later sub-periods start at a boundary's close and
            // own only the flows after it.
            var firstFlowDate = i == 0 ? startPoint.Date : startPoint.Date.AddDays(1);

            decimal netFlow = 0m;
            decimal weightedFlow = 0m;
            foreach (var cf in ctx.CashFlows)
            {
                if (cf.Date < firstFlowDate || cf.Date > endPoint.Date) continue;
                netFlow += cf.Amount;
                var dayFromStart = cf.Date.DayNumber - startPoint.Date.DayNumber;
                var weight = (decimal)(subDays - dayFromStart) / subDays;
                weightedFlow += weight * cf.Amount;
            }

            var denom = startPoint.Value + weightedFlow;
            if (denom == 0m)
                return new ReturnResult(null, MethodName, BasisName, "ZeroDenominator");

            var subReturn = (endPoint.Value - startPoint.Value - netFlow) / denom;
            cumulative *= 1m + subReturn;
        }

        return ReturnRates.Complete(new ReturnResult(cumulative - 1m, MethodName, BasisName, null), ctx.PeriodDays);
    }
}
