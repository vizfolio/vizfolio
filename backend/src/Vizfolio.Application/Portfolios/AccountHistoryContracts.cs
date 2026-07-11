namespace Vizfolio.Application.Portfolios;

public sealed record HistoryCoverageResult(
    Guid AccountId,
    DateOnly? FirstTransactionDate,
    DateOnly? EarliestSnapshotDate,
    bool HasHistoryGap,
    DateOnly? SuggestedOpeningDate,
    int OpeningBalanceSnapshotCount,
    int StatementSnapshotCount,
    int BrokerPositionSnapshotCount);

public sealed record OpeningBalanceCommand(
    DateOnly AsOf,
    string? DefaultCurrencyCode,
    IReadOnlyList<OpeningBalanceHolding> Holdings);

public sealed record OpeningBalanceHolding(
    string Symbol,
    decimal Units,
    decimal? MarketValue,
    decimal? UnitPrice,
    decimal? CostBasis,
    string? CurrencyCode,
    string? Cusip);

public sealed record OpeningBalanceResult(
    Guid AccountId,
    DateOnly AsOf,
    int SnapshotsCreated,
    int SnapshotsUpdated,
    IReadOnlyList<OpeningBalanceHoldingOutcome> Holdings);

public sealed record OpeningBalanceHoldingOutcome(
    string Symbol,
    Guid AccountHoldingId,
    decimal Quantity,
    decimal? MarketValue,
    decimal? UnitPrice,
    decimal? CostBasis,
    string? CurrencyCode,
    bool Created);
