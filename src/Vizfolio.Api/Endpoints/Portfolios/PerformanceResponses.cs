namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record PortfolioPerformanceResponse(
    DateOnly From,
    DateOnly To,
    PerformanceBalance StartingBalance,
    PerformanceBalance EndingBalance,
    string CurrencyCode);

public sealed record PerformanceBalance(
    decimal Value,
    bool IsComplete,
    DateOnly? SnapshotAsOf,
    int HoldingsCovered,
    int HoldingsMissingSnapshot);
