namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedAccountStatement(
    string? InstitutionCode,
    string? AccountNumber,
    IReadOnlyList<ParsedTransaction> Transactions,
    IReadOnlyList<ParsedPosition> Positions,
    DateOnly? AsOf,
    // The statement's cash balances (OFX <INVBAL>), when the format reports them.
    ParsedCashBalance? Cash = null);

/// <summary>
/// The cash balances a statement reports (OFX <c>&lt;INVBAL&gt;</c>) at <see cref="AsOf"/>.
/// <see cref="IncludesSettlementFund"/> is true when <see cref="AvailableCash"/> is the settlement fund's position (or
/// already contains it) — the broker profile or the statement itself says so — so the position, not this balance,
/// anchors the account's cash and the two are never counted twice.
/// </summary>
public sealed record ParsedCashBalance(
    DateOnly AsOf,
    decimal AvailableCash,
    decimal? MarginBalance,
    decimal? ShortBalance,
    bool IncludesSettlementFund);
