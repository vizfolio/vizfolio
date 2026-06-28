using System.Text.Json.Serialization;

namespace Vizfolio.Application.Extracts.Models;

public sealed record SecurityExtract(
    [property: JsonPropertyName("cik")] string Cik,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("entity_type")] string? EntityType,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("sector")] string? Sector,
    [property: JsonPropertyName("state_of_incorporation")] string? StateOfIncorporation,
    [property: JsonPropertyName("sic")] string? Sic,
    [property: JsonPropertyName("sic_description")] string? SicDescription,
    [property: JsonPropertyName("tickers")] IReadOnlyList<string>? Tickers,
    [property: JsonPropertyName("exchanges")] IReadOnlyList<string>? Exchanges,
    [property: JsonPropertyName("schema_version")] string? SchemaVersion,
    [property: JsonPropertyName("source")] SecurityExtractSource Source);

public sealed record SecurityExtractSource(
    [property: JsonPropertyName("edgar_fetched_at")] DateTimeOffset EdgarFetchedAt);
