namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record PortfolioPerformanceResponse(
    DateOnly From,
    DateOnly To,
    PerformanceBalance StartingBalance,
    PerformanceBalance EndingBalance,
    PerformanceContributions Contributions,
    PerformanceReturns Returns,
    string CurrencyCode);

public sealed record PerformanceBalance(
    decimal Value,
    bool IsComplete,
    DateOnly? SnapshotAsOf,
    int HoldingsCovered,
    int HoldingsMissingSnapshot);

public sealed record PerformanceContributions(
    decimal Net,
    decimal Deposits,
    decimal Withdrawals,
    int Count);

public sealed record PerformanceReturns(
    PerformanceReturn TimeWeighted,
    PerformanceReturn MoneyWeighted);

public sealed record PerformanceReturn(
    decimal? Rate,
    string Method,
    string Basis,
    string? Reason);
