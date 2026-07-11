namespace Vizfolio.Application.Portfolios;

public sealed record PortfolioPerformanceResult(
    DateOnly From,
    DateOnly To,
    PerformanceBalanceResult StartingBalance,
    PerformanceBalanceResult EndingBalance,
    PerformanceContributionsResult Contributions,
    PerformanceReturnsResult Returns,
    string CurrencyCode);

public sealed record PerformanceBalanceResult(
    decimal Value,
    bool IsComplete,
    DateOnly? SnapshotAsOf,
    int HoldingsCovered,
    int HoldingsMissingSnapshot);

public sealed record PerformanceContributionsResult(
    decimal Net,
    decimal Deposits,
    decimal Withdrawals,
    int Count);

public sealed record PerformanceReturnsResult(
    ReturnResult TimeWeighted,
    ReturnResult MoneyWeighted);

public sealed record ReturnResult(
    decimal? Rate,
    string Method,
    string Basis,
    string? Reason);
