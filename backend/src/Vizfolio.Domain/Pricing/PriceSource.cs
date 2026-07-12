namespace Vizfolio.Domain.Pricing;

/// <summary>
/// The provider a price row (or corporate action) was fetched from. Stored as a string so the set
/// can grow without a migration.
/// </summary>
public enum PriceSource
{
    Stooq,
    Eodhd,
    AlphaVantage
}
