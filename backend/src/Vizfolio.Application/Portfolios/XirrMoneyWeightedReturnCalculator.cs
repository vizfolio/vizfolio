namespace Vizfolio.Application.Portfolios;

// True IRR of the cash-flow stream: solves NPV(r) = 0 with per-day discounting.
// Cash-flow signs are from the investor's perspective:
//   - StartingBalance and each contribution deposit are outflows (negative).
//   - Withdrawals and EndingBalance are inflows (positive).
// Bisection is used for robustness — XIRR can have multiple roots for pathological
// flow patterns; when there is no sign change the response Reason is "NoSignChange".
public sealed class XirrMoneyWeightedReturnCalculator : IMoneyWeightedReturnCalculator
{
    private const string MethodName = "XIRR";
    private const string BasisName = "Annualized";
    private const double LowerBound = -0.9999;
    private const double UpperBound = 100.0;
    private const double Tolerance = 1e-9;
    private const int MaxIterations = 200;

    public ReturnResult Compute(PerformanceComputationContext ctx)
    {
        if (!ctx.StartingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteStartingBalance");
        if (!ctx.EndingIsComplete)
            return new ReturnResult(null, MethodName, BasisName, "IncompleteEndingBalance");

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
            return new ReturnResult(null, MethodName, BasisName, "InsufficientCashFlows");

        var hasPositive = consolidated.Any(f => f.Amount > 0m);
        var hasNegative = consolidated.Any(f => f.Amount < 0m);
        if (!hasPositive || !hasNegative)
            return new ReturnResult(null, MethodName, BasisName, "NoSignChange");

        var t0 = consolidated[0].Date;

        double Npv(double r)
        {
            if (r <= -1.0) return double.MaxValue;
            double sum = 0;
            foreach (var cf in consolidated)
            {
                var years = (cf.Date.DayNumber - t0.DayNumber) / 365.0;
                sum += (double)cf.Amount / Math.Pow(1.0 + r, years);
            }
            return sum;
        }

        double lo = LowerBound, hi = UpperBound;
        double npvLo = Npv(lo), npvHi = Npv(hi);
        if (npvLo * npvHi > 0)
            return new ReturnResult(null, MethodName, BasisName, "DidNotConverge");

        for (int i = 0; i < MaxIterations; i++)
        {
            var mid = (lo + hi) / 2.0;
            var npvMid = Npv(mid);
            if (Math.Abs(npvMid) < Tolerance || (hi - lo) < Tolerance)
                return new ReturnResult((decimal)mid, MethodName, BasisName, null);

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

        return new ReturnResult((decimal)((lo + hi) / 2.0), MethodName, BasisName, null);
    }
}
