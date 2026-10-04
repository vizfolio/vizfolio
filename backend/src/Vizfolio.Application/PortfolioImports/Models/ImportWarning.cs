namespace Vizfolio.Application.PortfolioImports.Models;

/// <summary>
/// Something in an imported file that wasn't fully understood — an unmapped label, an unknown OFX aggregate, a row
/// that failed — grouped by <see cref="Code"/> and <see cref="Message"/>, with a few sample rows. Codes are listed in
/// <see cref="ImportWarningCodes"/>.
/// </summary>
public sealed record ImportWarning(string Code, string Message, int Count, IReadOnlyList<string> Samples);

/// <summary>The machine-readable codes of <see cref="ImportWarning"/> (the UI maps them to help text).</summary>
public static class ImportWarningCodes
{
    /// <summary>A broker label with no mapping; the row was stored as <c>Other</c>.</summary>
    public const string UnmappedLabel = "UnmappedLabel";

    /// <summary>An OFX aggregate the parser doesn't know; its rows were not imported.</summary>
    public const string UnknownAggregate = "UnknownAggregate";

    /// <summary>A row that couldn't be read (e.g. a bad date) and was skipped; the rest of the file still imported.</summary>
    public const string RowFailed = "RowFailed";

    /// <summary>A statement position that names no security; it was not recorded.</summary>
    public const string PositionUnresolved = "PositionUnresolved";

    /// <summary>A split reported with no ratio; only its change in shares (if any) is applied.</summary>
    public const string SplitWithoutRatio = "SplitWithoutRatio";

    /// <summary>A security identified only by an ID type Vizfolio can't look up (e.g. an ISIN with no ticker).</summary>
    public const string UnsupportedSecurityId = "UnsupportedSecurityId";

    /// <summary>Options were imported; they can't be priced, so the account is valued as incomplete while they're held.</summary>
    public const string OptionActivity = "OptionActivity";

    /// <summary>The statement reports a margin or short balance, which valuation doesn't model.</summary>
    public const string MarginBalance = "MarginBalance";

    /// <summary>A statement in the file belongs to a different account than the one imported into; it was skipped.</summary>
    public const string OtherAccountSkipped = "OtherAccountSkipped";
}

/// <summary>Collects warnings while parsing or importing, merging repeats of the same code and message.</summary>
public sealed class ImportWarningCollector
{
    private const int MaxSamples = 5;
    private readonly Dictionary<(string Code, string Message), (int Count, List<string> Samples)> _warnings = new();
    private readonly List<(string Code, string Message)> _order = [];

    public void Add(string code, string message, string? sample = null)
    {
        var key = (code, message);
        if (!_warnings.TryGetValue(key, out var entry))
        {
            entry = (0, []);
            _order.Add(key);
        }

        if (sample is not null && entry.Samples.Count < MaxSamples && !entry.Samples.Contains(sample))
            entry.Samples.Add(sample);
        _warnings[key] = (entry.Count + 1, entry.Samples);
    }

    public void AddRange(IEnumerable<ImportWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            var key = (warning.Code, warning.Message);
            if (!_warnings.TryGetValue(key, out var entry))
            {
                entry = (0, []);
                _order.Add(key);
            }

            foreach (var sample in warning.Samples)
                if (entry.Samples.Count < MaxSamples && !entry.Samples.Contains(sample))
                    entry.Samples.Add(sample);
            _warnings[key] = (entry.Count + warning.Count, entry.Samples);
        }
    }

    public IReadOnlyList<ImportWarning> ToList()
        => _order.Select(k => new ImportWarning(k.Code, k.Message, _warnings[k].Count, _warnings[k].Samples.ToList())).ToList();
}
