using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

internal static class PerformanceMapping
{
    public static PortfolioPerformanceResponse ToResponse(PortfolioPerformanceResult result) =>
        new(
            result.From,
            result.To,
            ToBalance(result.StartingBalance),
            ToBalance(result.EndingBalance),
            ToContributions(result.Contributions),
            ToReturns(result.Returns),
            result.CurrencyCode,
            ToSeries(result.Series));

    private static PerformanceBalance ToBalance(PerformanceBalanceResult r) =>
        new(r.Value, r.IsComplete, r.SnapshotAsOf, r.HoldingsCovered, r.HoldingsMissingSnapshot,
            r.Missing.Select(m => new PerformanceMissing(m.AccountId, m.HoldingId, m.Symbol, m.Cause)).ToList());

    private static PerformanceContributions ToContributions(PerformanceContributionsResult c) =>
        new(c.Net, c.Deposits, c.Withdrawals, c.Count);

    private static PerformanceReturns ToReturns(PerformanceReturnsResult r) =>
        new(ToReturn(r.TimeWeighted), ToReturn(r.MoneyWeighted));

    private static PerformanceReturn ToReturn(ReturnResult r) =>
        new(r.Rate, r.Method, r.Basis, r.Reason, r.AnnualizedRate, r.FallbackReason);

    private static PerformanceSeries ToSeries(PerformanceSeriesResult s) =>
        new(s.Interval.ToString(), s.Points.Select(p => new PerformanceSeriesPointResponse(
            p.Date, p.Value, p.Deposits, p.Withdrawals, p.CumulativeReturn, p.InvestmentGain)).ToList());
}
