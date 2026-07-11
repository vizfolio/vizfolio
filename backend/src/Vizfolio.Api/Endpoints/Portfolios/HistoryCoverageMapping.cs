using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

internal static class HistoryCoverageMapping
{
    public static HistoryCoverageResponse ToResponse(HistoryCoverageResult r) =>
        new(
            r.AccountId,
            r.FirstTransactionDate,
            r.EarliestSnapshotDate,
            r.HasHistoryGap,
            r.SuggestedOpeningDate,
            r.OpeningBalanceSnapshotCount,
            r.StatementSnapshotCount,
            r.BrokerPositionSnapshotCount);

    public static OpeningBalanceResponse ToResponse(OpeningBalanceResult r) =>
        new(
            r.AccountId,
            r.AsOf,
            r.SnapshotsCreated,
            r.SnapshotsUpdated,
            r.Holdings.Select(ToResponse).ToList());

    private static OpeningBalanceHoldingOutcomeResponse ToResponse(OpeningBalanceHoldingOutcome h) =>
        new(h.Symbol, h.AccountHoldingId, h.Quantity, h.MarketValue, h.UnitPrice, h.CostBasis, h.CurrencyCode, h.Created);
}
