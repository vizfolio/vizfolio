using System.Text.Json.Serialization;

namespace Vizfolio.Application.Extracts.Models;

/// <summary>
/// <c>money_market_funds.json</c> from fund-extracts: every money market fund, built from EDGAR Form N-MFP filings
/// and SEC's mutual-fund ticker map (see the edgar-extract pipeline's <c>money_market</c> module).
/// </summary>
public sealed record MoneyMarketRegistryExtract(
    [property: JsonPropertyName("schema_version")] string? SchemaVersion,
    [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
    [property: JsonPropertyName("funds")] IReadOnlyList<MoneyMarketFundExtract> Funds);

public sealed record MoneyMarketFundExtract(
    [property: JsonPropertyName("series_id")] string SeriesId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("registrant_cik")] string? RegistrantCik,
    [property: JsonPropertyName("as_of")] DateOnly AsOf,
    [property: JsonPropertyName("source_filing")] string SourceFiling,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("seeks_stable_price")] bool SeeksStablePrice,
    [property: JsonPropertyName("stable_price_per_share")] decimal? StablePricePerShare,
    [property: JsonPropertyName("is_retail")] bool? IsRetail,
    [property: JsonPropertyName("classes")] IReadOnlyList<MoneyMarketClassExtract>? Classes);

public sealed record MoneyMarketClassExtract(
    [property: JsonPropertyName("class_id")] string ClassId,
    [property: JsonPropertyName("ticker")] string? Ticker);
