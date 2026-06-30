namespace Vizfolio.Application.Performance.RateOfReturn;

public static class InternalRateOfReturnCalculator
{
    private const int MaxIterations = 50;
    private const double Tolerance = 1e-7;

    /// <summary>
    /// Annualized money-weighted return (XIRR-style). Returns null if Newton's method fails to
    /// converge or the cash-flow series has all same-signed flows (no solvable IRR).
    /// </summary>
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

        // Investor-perspective flows: invest Begin at t=0, invest each F_i at t_i, receive End at t=T.
        // Flow into portfolio (positive F) is investment from investor → negative IRR cashflow.
        var series = new List<(double Days, double Amount)>(flows.Count + 2)
        {
            (0, -(double)beginningBalance)
        };
        foreach (var flow in flows)
        {
            var days = flow.Date.DayNumber - periodStart.DayNumber;
            if (days < 0) days = 0;
            if (days > totalDays) days = totalDays;
            series.Add((days, -(double)flow.Amount));
        }
        series.Add((totalDays, (double)endingBalance));

        // Require at least one positive and one negative cashflow.
        var hasPositive = series.Any(s => s.Amount > 0);
        var hasNegative = series.Any(s => s.Amount < 0);
        if (!hasPositive || !hasNegative)
            return null;

        double rate = 0.1;
        for (var iter = 0; iter < MaxIterations; iter++)
        {
            var (npv, derivative) = NpvAndDerivative(series, rate);
            if (double.IsNaN(npv) || double.IsInfinity(npv))
                return null;
            if (Math.Abs(npv) < Tolerance)
                return (decimal)rate;
            if (derivative == 0)
                return null;

            var nextRate = rate - npv / derivative;
            if (nextRate <= -1.0)
                nextRate = (rate - 1.0) / 2.0;

            if (Math.Abs(nextRate - rate) < Tolerance)
                return (decimal)nextRate;

            rate = nextRate;
        }

        return null;
    }

    private static (double Npv, double Derivative) NpvAndDerivative(
        List<(double Days, double Amount)> series, double rate)
    {
        double npv = 0;
        double derivative = 0;
        var oneePlusR = 1.0 + rate;

        foreach (var (days, amount) in series)
        {
            var years = days / 365.0;
            var discount = Math.Pow(oneePlusR, years);
            npv += amount / discount;
            derivative += -years * amount / (discount * oneePlusR);
        }

        return (npv, derivative);
    }
}
