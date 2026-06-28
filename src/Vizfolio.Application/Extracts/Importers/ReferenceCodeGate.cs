using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Importers;

/// <summary>
/// Validates incoming reference codes (currency, country, asset category) against
/// the canonical lookup tables. Unknown values are dropped (returned as null) and
/// recorded so the run can surface a data-cleaning report — the caller can then
/// have the upstream extract fixed.
/// </summary>
internal sealed class ReferenceCodeGate
{
    public const string CurrencyField = "Currency";
    public const string CountryField = "Country";
    public const string AssetCategoryField = "AssetCategory";

    private readonly IReadOnlySet<string> _currencies;
    private readonly IReadOnlySet<string> _countries;
    private readonly IReadOnlySet<string> _assetCategories;
    private readonly Dictionary<(string Field, string Value), int> _cleaned = new();

    private ReferenceCodeGate(
        IReadOnlySet<string> currencies,
        IReadOnlySet<string> countries,
        IReadOnlySet<string> assetCategories)
    {
        _currencies = currencies;
        _countries = countries;
        _assetCategories = assetCategories;
    }

    public static async Task<ReferenceCodeGate> LoadAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var currencies = await db.Currencies.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken);
        var countries = await db.Countries.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken);
        var categories = await db.AssetCategories.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken);

        return new ReferenceCodeGate(
            new HashSet<string>(currencies, StringComparer.Ordinal),
            new HashSet<string>(countries, StringComparer.Ordinal),
            new HashSet<string>(categories, StringComparer.Ordinal));
    }

    public string? AcceptCurrency(string? raw) => Accept(_currencies, CurrencyField, raw);

    public string? AcceptCountry(string? raw) => Accept(_countries, CountryField, raw);

    public string? AcceptAssetCategory(string? raw) => Accept(_assetCategories, AssetCategoryField, raw);

    public IReadOnlyList<DataCleaningEntry> BuildReport() => _cleaned
        .Select(kvp => new DataCleaningEntry(
            Field: kvp.Key.Field,
            OriginalValue: kvp.Key.Value,
            Reason: $"Unknown {kvp.Key.Field.ToLowerInvariant()} code; value dropped (set to null).",
            Occurrences: kvp.Value))
        .OrderBy(e => e.Field, StringComparer.Ordinal)
        .ThenBy(e => e.OriginalValue, StringComparer.Ordinal)
        .ToList();

    public void LogReport<T>(ILogger<T> logger)
    {
        foreach (var entry in BuildReport())
        {
            logger.LogWarning(
                "Import dropped unknown {Field} code {Code} ({Occurrences} occurrence(s)); fix upstream extract.",
                entry.Field, entry.OriginalValue, entry.Occurrences);
        }
    }

    private string? Accept(IReadOnlySet<string> set, string field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var normalized = raw.Trim().ToUpperInvariant();
        if (set.Contains(normalized)) return normalized;

        var key = (field, normalized);
        _cleaned.TryGetValue(key, out var count);
        _cleaned[key] = count + 1;
        return null;
    }
}
