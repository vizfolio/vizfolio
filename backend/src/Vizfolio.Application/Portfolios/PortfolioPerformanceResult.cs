namespace Vizfolio.Application.Portfolios;

public sealed record PortfolioPerformanceResult(
    DateOnly From,
    DateOnly To,
    PerformanceBalanceResult StartingBalance,
    PerformanceBalanceResult EndingBalance,
    PerformanceContributionsResult Contributions,
    PerformanceReturnsResult Returns,
    string CurrencyCode,
    PerformanceSeriesResult Series);

/// <summary>
/// A balance at a date. <see cref="HoldingsMissingSnapshot"/> counts the holdings (and cash) that couldn't be valued;
/// <see cref="Missing"/> says which and why (up to <see cref="PerformanceBalanceResult.MaxMissingDetails"/>).
/// </summary>
public sealed record PerformanceBalanceResult(
    decimal Value,
    bool IsComplete,
    DateOnly? SnapshotAsOf,
    int HoldingsCovered,
    int HoldingsMissingSnapshot)
{
    public const int MaxMissingDetails = 10;

    public IReadOnlyList<MissingValuation> Missing { get; init; } = [];
}

/// <summary>Something that couldn't be valued on a date: a holding, or the account's cash (null symbol).</summary>
public sealed record MissingValuation(Guid AccountId, Guid? HoldingId, string? Symbol, string Cause);

public sealed record PerformanceContributionsResult(
    decimal Net,
    decimal Deposits,
    decimal Withdrawals,
    int Count);

public sealed record PerformanceReturnsResult(
    ReturnResult TimeWeighted,
    ReturnResult MoneyWeighted);

/// <summary>
/// One return figure. <see cref="Rate"/> is null when it can't be computed, and <see cref="Reason"/> then says why;
/// its <see cref="Basis"/> says which form it is in. Both forms are also given explicitly:
/// <see cref="PeriodRate"/>, the total over the period, and <see cref="AnnualizedRate"/>, the per-year rate that
/// compounds to it (only for periods of a year or longer) — see <see cref="ReturnRates"/>.
/// <see cref="FallbackReason"/> is set when the preferred method couldn't be used and <see cref="Method"/> names the
/// approximation that was used instead (e.g. a daily-valued TWR falling back to Modified Dietz because a day in the
/// period couldn't be valued).
/// </summary>
public sealed record ReturnResult(
    decimal? Rate,
    string Method,
    string Basis,
    string? Reason)
{
    /// <summary><see cref="Basis"/> of a rate that is per year (the XIRR over a year or more).</summary>
    public const string AnnualizedBasis = "Annualized";

    /// <summary><see cref="Basis"/> of a rate that is the total over the period.</summary>
    public const string PeriodBasis = "Period";

    /// <summary>The per-year rate, for periods of a year or longer; null for shorter periods.</summary>
    public decimal? AnnualizedRate { get; init; }

    /// <summary>The total return over the whole period (how much the money grew, start to end).</summary>
    public decimal? PeriodRate { get; init; }

    public string? FallbackReason { get; init; }
}
