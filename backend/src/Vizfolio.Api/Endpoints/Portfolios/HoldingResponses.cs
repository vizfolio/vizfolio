namespace Vizfolio.Api.Endpoints.Portfolios;

/// <summary>
/// A single position in an account on the requested <c>asOf</c> date, valued by the same rules as the
/// performance balances (docs/price-history-valuation.md): ledger quantity × a recent close, $1.00 a share for a
/// money-market fund, or the broker's snapshot as a fallback.
/// <para>
/// <see cref="Status"/> is <c>Valued</c>, <c>NotHeld</c> (a true $0 on that date) or <c>Missing</c> (held, but
/// there's no recent price or snapshot to value it — <see cref="MarketValue"/> is null). <see cref="ValuationSource"/>
/// says where a value came from (<c>Price</c>, <c>Snapshot</c> or <c>StableNav</c>) and <see cref="PriceAsOf"/> the
/// date of that price or snapshot. <see cref="HasSnapshot"/>, <see cref="SnapshotAsOf"/> and <see cref="Source"/>
/// describe the latest snapshot on or before <c>asOf</c>, whether or not it was used. <see cref="CostBasis"/> comes
/// from that snapshot and is only reported while the position still matches it (no shares bought or sold since).
/// </para>
/// <para>
/// The account's cash — uninvested cash plus its settlement fund (e.g. VMFXX) — is one row with <see cref="Kind"/>
/// <c>Cash</c>, listed last; the settlement fund itself isn't listed separately.
/// </para>
/// </summary>
public sealed record HoldingResponse(
    Guid AccountHoldingId,
    string Kind,
    string? Symbol,
    string? Name,
    string? Cusip,
    string? Isin,
    string? CurrencyCode,
    bool HasSnapshot,
    DateOnly? SnapshotAsOf,
    string? Source,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? MarketValue,
    decimal? CostBasis,
    decimal? GainLoss,
    string Status,
    string? ValuationSource,
    DateOnly? PriceAsOf);
