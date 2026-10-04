namespace Vizfolio.Application.Portfolios;

// True IRR of the cash-flow stream: solves NPV(r) = 0 with per-day discounting.
// Periods of a year or more report the annualized rate as Rate. Shorter periods report the rate compounded over the
// period instead (basis "Period"): annualizing a few weeks' gain (3% in two weeks → ~115% a year) misleads.
// Either way PeriodRate is the total over the cash flows' span — the annual rate compounded from the first flow to the
// last — so a long period can show both "x% a year" and "y% in total" (see ReturnRates).
// Cash-flow signs are from the investor's perspective:
//   - StartingBalance and each contribution deposit are outflows (negative).
//   - Withdrawals and EndingBalance are inflows (positive).
// Bisection is used for robustness — XIRR can have multiple roots for pathological
// flow patterns; when there is no sign change the response Reason is "NoSignChange".
public sealed class XirrMoneyWeightedReturnCalculator : IMoneyWeightedReturnCalculator
{
    private const string MethodName = "XIRR";
    private const string AnnualizedBasis = ReturnResult.AnnualizedBasis;
    private const string PeriodBasis = ReturnResult.PeriodBasis;
    private const int DaysPerYear = ReturnRates.DaysPerYear;
    private const double LowerBound = -0.9999;
    private const double UpperBound = 100.0;
    private const double Tolerance = 1e-9;
    private const int MaxIterations = 200;

    public ReturnResult Compute(PerformanceComputationContext ctx)
    {
        var periodDays = ctx.To.DayNumber - ctx.From.DayNumber;
        var basisName = periodDays < DaysPerYear ? PeriodBasis : AnnualizedBasis;

        if (!ctx.StartingIsComplete)
            return new ReturnResult(null, MethodName, basisName, "IncompleteStartingBalance");
        if (!ctx.EndingIsComplete)
            return new ReturnResult(null, MethodName, basisName, "IncompleteEndingBalance");

        var flows = new List<CashFlow>(ctx.CashFlows.Count + 2)
        {
            new(ctx.From, -ctx.StartingBalance),
        };
        foreach (var cf in ctx.CashFlows)
            flows.Add(new CashFlow(cf.Date, -cf.Amount));
        flows.Add(new CashFlow(ctx.To, ctx.EndingBalance));

        var consolidated = flows
            .GroupBy(f => f.Date)
            .Select(g => new CashFlow(g.Key, g.Sum(x => x.Amount)))
            .Where(f => f.Amount != 0m)
            .OrderBy(f => f.Date)
            .ToList();

        if (consolidated.Count < 2)
            return new ReturnResult(null, MethodName, basisName, "InsufficientCashFlows");

        var hasPositive = consolidated.Any(f => f.Amount > 0m);
        var hasNegative = consolidated.Any(f => f.Amount < 0m);
        if (!hasPositive || !hasNegative)
            return new ReturnResult(null, MethodName, basisName, "NoSignChange");

        var t0 = consolidated[0].Date;

        // Solve in the units the rate is reported in: per year for a year or more; for a shorter period, per the
        // flows' own span — which is the period return directly, and keeps a short, large move from overflowing
        // the solver's bounds once annualized.
        var spanDays = consolidated[^1].Date.DayNumber - t0.DayNumber;
        var unitDays = basisName == AnnualizedBasis ? DaysPerYear : Math.Max(spanDays, 1);
        ReturnResult Solved(double rate)
        {
            var solved = new ReturnResult((decimal)rate, MethodName, basisName, null);
            return ReturnRates.Complete(
                basisName == AnnualizedBasis ? solved with { PeriodRate = ReturnRates.Compound((decimal)rate, spanDays) } : solved,
                periodDays);
        }

        double Npv(double r)
        {
            if (r <= -1.0) return double.MaxValue;
            double sum = 0;
            foreach (var cf in consolidated)
            {
                var units = (cf.Date.DayNumber - t0.DayNumber) / (double)unitDays;
                sum += (double)cf.Amount / Math.Pow(1.0 + r, units);
            }
            return sum;
        }

        double lo = LowerBound, hi = UpperBound;
        double npvLo = Npv(lo), npvHi = Npv(hi);
        if (npvLo * npvHi > 0)
            return new ReturnResult(null, MethodName, basisName, "DidNotConverge");

        for (int i = 0; i < MaxIterations; i++)
        {
            var mid = (lo + hi) / 2.0;
            var npvMid = Npv(mid);
            if (Math.Abs(npvMid) < Tolerance || (hi - lo) < Tolerance)
                return Solved(mid);

            if (npvLo * npvMid < 0)
            {
                hi = mid;
                npvHi = npvMid;
            }
            else
            {
                lo = mid;
                npvLo = npvMid;
            }
        }

        return Solved((lo + hi) / 2.0);
    }
}
