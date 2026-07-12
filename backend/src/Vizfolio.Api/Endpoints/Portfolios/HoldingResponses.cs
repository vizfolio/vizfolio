namespace Vizfolio.Api.Endpoints.Portfolios;

/// <summary>
/// A single position in an account, valued from the most recent holding snapshot on or before
/// the requested <c>asOf</c> date. When no such snapshot exists, <see cref="HasSnapshot"/> is
/// false and the valuation fields are null.
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
    decimal? GainLoss);
