using System.Text.Json.Serialization;

namespace Vizfolio.Application.Extracts.Models;

public sealed record FundsManifest(
    [property: JsonPropertyName("schema_version")] string? SchemaVersion,
    [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
    [property: JsonPropertyName("funds")] IReadOnlyList<FundsManifestEntry> Funds);

public sealed record FundsManifestEntry(
    [property: JsonPropertyName("series_id")] string SeriesId,
    [property: JsonPropertyName("latest_period")] string LatestPeriod,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("registrant_cik")] string? RegistrantCik,
    [property: JsonPropertyName("latest_accession")] string? LatestAccession,
    [property: JsonPropertyName("tickers")] IReadOnlyList<string>? Tickers);
