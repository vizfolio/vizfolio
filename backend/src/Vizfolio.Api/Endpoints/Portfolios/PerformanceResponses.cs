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

/// <summary>
/// A balance at a date. <see cref="Missing"/> lists (up to 10) holdings that couldn't be valued — a null symbol is
/// the account's cash — with the cause: <c>NoPrice</c>, <c>StalePrice</c>, <c>NegativePosition</c>,
/// <c>MaterialMismatch</c> or <c>BeforeHistory</c>.
/// </summary>
public sealed record PerformanceBalance(
    decimal Value,
    bool IsComplete,
    DateOnly? SnapshotAsOf,
    int HoldingsCovered,
    int HoldingsMissingSnapshot,
    IReadOnlyList<PerformanceMissing> Missing);

public sealed record PerformanceMissing(Guid AccountId, Guid? AccountHoldingId, string? Symbol, string Cause);

public sealed record PerformanceContributions(
    decimal Net,
    decimal Deposits,
    decimal Withdrawals,
    int Count);

public sealed record PerformanceReturns(
    PerformanceReturn TimeWeighted,
    PerformanceReturn MoneyWeighted);

/// <summary>
/// A return figure. <c>Rate</c> is null when it can't be computed (<c>Reason</c> says why); <c>Basis</c> says whether
/// it's per year ("Annualized") or the total ("Period"). Both forms are also given explicitly: <c>PeriodRate</c> is the
/// total over the period and <c>AnnualizedRate</c> the per-year rate that compounds to it (periods of a year or more
/// only). <c>FallbackReason</c> is set when the preferred method couldn't be used and <c>Method</c> names the
/// approximation used instead.
/// </summary>
public sealed record PerformanceReturn(
    decimal? Rate,
    string Method,
    string Basis,
    string? Reason,
    decimal? AnnualizedRate,
    string? FallbackReason,
    decimal? PeriodRate);

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
