namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record HistoryCoverageResponse(
    Guid AccountId,
    DateOnly? FirstTransactionDate,
    DateOnly? EarliestSnapshotDate,
    bool HasHistoryGap,
    DateOnly? SuggestedOpeningDate,
    int OpeningBalanceSnapshotCount,
    int StatementSnapshotCount,
    int BrokerPositionSnapshotCount);

public sealed record SetOpeningBalanceRequest(
    Guid PortfolioId,
    Guid AccountId,
    DateOnly AsOf,
    string? DefaultCurrencyCode,
    IReadOnlyList<OpeningBalanceHoldingInput> Holdings);

public sealed record OpeningBalanceHoldingInput(
    string Symbol,
    decimal Units,
    decimal? MarketValue,
    decimal? UnitPrice,
    decimal? CostBasis,
    string? CurrencyCode,
    string? Cusip);

public sealed record OpeningBalanceResponse(
    Guid AccountId,
    DateOnly AsOf,
    int SnapshotsCreated,
    int SnapshotsUpdated,
    IReadOnlyList<OpeningBalanceHoldingOutcomeResponse> Holdings);

public sealed record OpeningBalanceHoldingOutcomeResponse(
    string Symbol,
    Guid AccountHoldingId,
    decimal Quantity,
    decimal? MarketValue,
    decimal? UnitPrice,
    decimal? CostBasis,
    string? CurrencyCode,
    bool Created);
