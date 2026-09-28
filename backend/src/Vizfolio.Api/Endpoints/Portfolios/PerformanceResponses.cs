namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record PortfolioPerformanceResponse(
    DateOnly From,
    DateOnly To,
    PerformanceBalance StartingBalance,
    PerformanceBalance EndingBalance,
    PerformanceContributions Contributions,
    PerformanceReturns Returns,
    string CurrencyCode,
    PerformanceSeries Series);

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

/// <summary>
/// Value / returns-over-time chart data. <c>Interval</c> is "Weekly", "Monthly" or "Quarterly". The first point is
/// the period's opening (starting balance); the last is <c>To</c> (ending balance).
/// </summary>
public sealed record PerformanceSeries(string Interval, IReadOnlyList<PerformanceSeriesPointResponse> Points);

/// <summary>
/// Balance at the close of <c>Date</c> (null when some holding couldn't be valued), the deposits /
/// withdrawals (negative) since the previous point, the cumulative time-weighted return from the period's
/// start (a decimal rate, same strategy as the headline) and the cumulative investment gain
/// (value − starting balance − net contributions to date). Return and gain are null when not computable.
/// </summary>
public sealed record PerformanceSeriesPointResponse(
    DateOnly Date,
    decimal? Value,
    decimal Deposits,
    decimal Withdrawals,
    decimal? CumulativeReturn,
    decimal? InvestmentGain);
