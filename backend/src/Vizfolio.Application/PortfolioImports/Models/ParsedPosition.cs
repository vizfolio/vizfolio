namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedPosition(
    DateOnly AsOf,
    string? Ticker,
    string? Cusip,
    decimal Units,
    decimal? UnitPrice,
    decimal? MarketValue,
    decimal? CostBasis,
    string? CurrencyCode,
    // The date the unit price is from (OFX DTPRICEASOF), when the format reports it.
    DateOnly? PriceAsOf = null,
    // The broker marks this position as the account's settlement (core / sweep) fund, i.e. its cash.
    bool IsSettlementFund = false);
