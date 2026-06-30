using System.Text.Json.Serialization;

namespace Vizfolio.Application.Extracts.Models;

public sealed record FundSnapshotExtract(
    [property: JsonPropertyName("schema_version")] string? SchemaVersion,
    [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
    [property: JsonPropertyName("fund")] FundExtract Fund,
    [property: JsonPropertyName("holdings")] IReadOnlyList<HoldingExtract>? Holdings);

public sealed record FundExtract(
    [property: JsonPropertyName("series_id")] string SeriesId,
    [property: JsonPropertyName("as_of")] DateOnly AsOf,
    [property: JsonPropertyName("source_filing")] string SourceFiling,
    [property: JsonPropertyName("source_url")] string SourceUrl,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("registrant_cik")] string? RegistrantCik,
    [property: JsonPropertyName("registrant_name")] string? RegistrantName,
    [property: JsonPropertyName("net_assets_usd")] decimal? NetAssetsUsd,
    [property: JsonPropertyName("total_assets_usd")] decimal? TotalAssetsUsd,
    [property: JsonPropertyName("total_liabs_usd")] decimal? TotalLiabilitiesUsd,
    [property: JsonPropertyName("cash_not_in_portfolio_usd")] decimal? CashNotInPortfolioUsd,
    [property: JsonPropertyName("is_final_filing")] bool IsFinalFiling,
    [property: JsonPropertyName("share_classes")] IReadOnlyList<ShareClassExtract>? ShareClasses,
    [property: JsonPropertyName("monthly_returns")] IReadOnlyList<MonthlyReturnExtract>? MonthlyReturns);

public sealed record ShareClassExtract(
    [property: JsonPropertyName("class_id")] string ClassId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("ticker")] string? Ticker,
    [property: JsonPropertyName("expense_ratio")] decimal? ExpenseRatio);

public sealed record MonthlyReturnExtract(
    [property: JsonPropertyName("month"), JsonConverter(typeof(MonthDateOnlyJsonConverter))] DateOnly Month,
    [property: JsonPropertyName("return_pct")] decimal ReturnPct,
    [property: JsonPropertyName("class_id")] string? ClassId);

public sealed record HoldingExtract(
    [property: JsonPropertyName("weight")] decimal Weight,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("ticker")] string? Ticker,
    [property: JsonPropertyName("isin")] string? Isin,
    [property: JsonPropertyName("issuer_cik")] string? IssuerCik,
    [property: JsonPropertyName("asset_category")] string? AssetCategory,
    [property: JsonPropertyName("asset_class")] string? AssetClass,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("balance")] decimal? Balance,
    [property: JsonPropertyName("fair_value_usd")] decimal? FairValueUsd);
