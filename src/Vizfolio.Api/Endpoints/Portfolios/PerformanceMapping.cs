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
            result.CurrencyCode);

    private static PerformanceBalance ToBalance(PerformanceBalanceResult r) =>
        new(r.Value, r.IsComplete, r.SnapshotAsOf, r.HoldingsCovered, r.HoldingsMissingSnapshot);
}
