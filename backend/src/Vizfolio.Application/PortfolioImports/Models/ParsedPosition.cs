namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedPosition(
    DateOnly AsOf,
    string? Ticker,
    string? Cusip,
    decimal Units,
    decimal? UnitPrice,
    decimal? MarketValue,
    decimal? CostBasis,
    string? CurrencyCode);
