namespace Vizfolio.Application.Portfolios;

public sealed record PortfolioPerformanceResult(
    DateOnly From,
    DateOnly To,
    PerformanceBalanceResult StartingBalance,
    PerformanceBalanceResult EndingBalance,
    string CurrencyCode);

public sealed record PerformanceBalanceResult(
    decimal Value,
    bool IsComplete,
    DateOnly? SnapshotAsOf,
    int HoldingsCovered,
    int HoldingsMissingSnapshot);
