namespace Vizfolio.Application.Portfolios;

/// <summary>
/// Converts a return between its two forms: the <b>total</b> over a period (how much the money grew, start to end)
/// and the <b>per-year</b> rate that compounds to that total. Per-year figures are only given for periods of a year
/// or more: annualizing a few weeks' gain (3% in two weeks → ~115% a year) misleads.
/// </summary>
public static class ReturnRates
{
    public const int DaysPerYear = 365;

    /// <summary>The per-year rate that compounds to <paramref name="periodRate"/> over <paramref name="days"/>; null under a year.</summary>
    public static decimal? Annualize(decimal? periodRate, int days)
    {
        if (periodRate is not { } r || days < DaysPerYear || r <= -1m) return null;
        return (decimal)(Math.Pow((double)(1m + r), DaysPerYear / (double)days) - 1.0);
    }

    /// <summary>The total over <paramref name="days"/> that <paramref name="annualRate"/> compounds to.</summary>
    public static decimal? Compound(decimal? annualRate, int days)
    {
        if (annualRate is not { } r || days < 0 || r <= -1m) return null;
        return (decimal)(Math.Pow((double)(1m + r), days / (double)DaysPerYear) - 1.0);
    }

    /// <summary>
    /// Fills in both forms of <paramref name="result"/> for a period of <paramref name="periodDays"/>:
    /// <see cref="ReturnResult.PeriodRate"/> always (when there's a rate) and <see cref="ReturnResult.AnnualizedRate"/>
    /// for a year or more. Values a calculator already set are kept (the XIRR compounds over its own cash-flow span).
    /// With no rate, neither is given.
    /// </summary>
    public static ReturnResult Complete(ReturnResult result, int periodDays)
    {
        if (result.Rate is not { } rate)
            return result with { PeriodRate = null, AnnualizedRate = null };

        return result.Basis == ReturnResult.AnnualizedBasis
            ? result with
            {
                AnnualizedRate = rate,
                PeriodRate = result.PeriodRate ?? Compound(rate, periodDays),
            }
            : result with
            {
                PeriodRate = rate,
                AnnualizedRate = result.AnnualizedRate ?? Annualize(rate, periodDays),
            };
    }
}
