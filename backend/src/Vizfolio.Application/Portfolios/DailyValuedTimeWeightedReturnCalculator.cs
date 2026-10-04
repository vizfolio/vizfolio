namespace Vizfolio.Application.Portfolios;

// True time-weighted return: the period is split at every date with an external cash flow, each sub-period's return
// is measured from actual valuations, and the sub-period returns are chained. The timing and size of deposits and
// withdrawals therefore don't affect the result — it's how the investments did, comparable to a fund's published
// return.
//
// Flows happen at the start of their day (the period itself opens at the close of `from − 1`), so for a sub-period
// starting on flow date dᵢ and ending at the close of the day before the next flow date:
//     rᵢ = V(close of next flow date − 1) / (V(close of dᵢ − 1) + Fᵢ) − 1
//     TWR = ∏(1 + rᵢ) − 1
// Sub-periods in which the scope is empty (nothing invested and nothing left at the end) are skipped, so an account
// emptied and refunded later doesn't distort the chain. When an interior boundary can't be valued (or the valuations
// contradict the flows), the result falls back to Modified Dietz over the whole period: Method says "ModifiedDietz"
// and FallbackReason says why, so the UI can label the figure approximate.
public sealed class DailyValuedTimeWeightedReturnCalculator : ITimeWeightedReturnCalculator
{
    public const string MethodName = "DailyValuedTWR";
    private const string BasisName = "Period";

    // A balance at or below a cent is an empty scope (rounding residue), not something to measure a return on.
    private const decimal EmptyBalance = 0.01m;

    private readonly ModifiedDietzTimeWeightedReturnCalculator _fallback = new();

    public ReturnResult Compute(PerformanceComputationContext ctx)
    {
        if (!ctx.StartingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteStartingBalance");
        if (!ctx.EndingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteEndingBalance");
        if (ctx.To < ctx.From)
            return new ReturnResult(null, MethodName, BasisName, "PeriodTooShort");

        var chain = Chain(ctx);
        if (chain.FallbackCause is { } cause)
        {
            var approximate = _fallback.Compute(ctx);
            return ReturnRates.Complete(approximate with { FallbackReason = cause }, ctx.PeriodDays);
        }

        if (chain.Rate is not { } rate)
            return new ReturnResult(null, MethodName, BasisName, "NoInvestedBalance");

        return ReturnRates.Complete(new ReturnResult(rate, MethodName, BasisName, null), ctx.PeriodDays);
    }

    private static ChainResult Chain(PerformanceComputationContext ctx)
    {
        var flowOn = ctx.CashFlows
            .Where(f => f.Date >= ctx.From && f.Date <= ctx.To)
            .GroupBy(f => f.Date)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Amount));
        // A date whose flows net to zero (e.g. both legs of an internal transfer) doesn't change the chain.
        var starts = flowOn
            .Where(kv => kv.Value != 0m && kv.Key > ctx.From)
            .Select(kv => kv.Key)
            .Prepend(ctx.From)
            .Order()
            .ToList();

        if (starts.Count > 1 && ctx.ValueAt is null) return ChainResult.Fallback("NoDailyValuation");

        var growth = 1m;
        var measured = false;
        var startValue = ctx.StartingBalance;
        for (var i = 0; i < starts.Count; i++)
        {
            decimal endValue;
            if (i == starts.Count - 1)
            {
                endValue = ctx.EndingBalance;
            }
            else
            {
                var end = ctx.ValueAt!(starts[i + 1].AddDays(-1));
                if (!end.IsComplete) return ChainResult.Fallback(CauseOf(end));
                endValue = end.Value;
            }

            var invested = startValue + flowOn.GetValueOrDefault(starts[i]);
            var endsEmpty = endValue <= EmptyBalance;
            if (invested <= EmptyBalance)
            {
                if (!endsEmpty) return ChainResult.Fallback("ValueWithoutInvestment");
            }
            else if (endsEmpty)
            {
                // Everything invested vanished without a withdrawal: the valuation and the flows disagree.
                return ChainResult.Fallback("ValueVanished");
            }
            else
            {
                growth *= endValue / invested;
                measured = true;
            }

            startValue = endValue;
        }

        return measured ? new ChainResult(growth - 1m, null) : new ChainResult(null, null);
    }

    private static string CauseOf(PerformanceBalanceResult balance)
        => balance.Missing.FirstOrDefault()?.Cause ?? "IncompleteValuation";


    private sealed record ChainResult(decimal? Rate, string? FallbackCause)
    {
        public static ChainResult Fallback(string cause) => new(null, cause);
    }
}
