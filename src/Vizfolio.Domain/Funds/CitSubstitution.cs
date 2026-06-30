namespace Vizfolio.Domain.Funds;

public sealed class CitSubstitution
{
    private readonly List<string> _patterns = [];

    private CitSubstitution() { }

    public CitSubstitution(
        string substituteTicker,
        string substituteName,
        CitSubstitutionFidelity fidelity,
        string? note,
        string? citName = null)
    {
        if (string.IsNullOrWhiteSpace(substituteTicker))
            throw new ArgumentException("Substitute ticker is required.", nameof(substituteTicker));
        if (string.IsNullOrWhiteSpace(substituteName))
            throw new ArgumentException("Substitute name is required.", nameof(substituteName));

        CitSubstitutionId = Guid.NewGuid();
        SubstituteTicker = substituteTicker.Trim().ToUpperInvariant();
        SubstituteName = substituteName.Trim();
        Fidelity = fidelity;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CitName = string.IsNullOrWhiteSpace(citName) ? null : citName.Trim();
    }

    public Guid CitSubstitutionId { get; private set; }

    public string SubstituteTicker { get; private set; } = string.Empty;

    public string SubstituteName { get; private set; } = string.Empty;

    public CitSubstitutionFidelity Fidelity { get; private set; }

    public string? Note { get; private set; }

    public string? CitName { get; private set; }

    public IReadOnlyList<string> Patterns => _patterns;

    public void UpdateSubstitute(string substituteTicker, string substituteName)
    {
        if (string.IsNullOrWhiteSpace(substituteTicker))
            throw new ArgumentException("Substitute ticker is required.", nameof(substituteTicker));
        if (string.IsNullOrWhiteSpace(substituteName))
            throw new ArgumentException("Substitute name is required.", nameof(substituteName));

        SubstituteTicker = substituteTicker.Trim().ToUpperInvariant();
        SubstituteName = substituteName.Trim();
    }

    public void SetFidelity(CitSubstitutionFidelity fidelity) => Fidelity = fidelity;

    public void SetNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public void SetCitName(string? citName) => CitName = string.IsNullOrWhiteSpace(citName) ? null : citName.Trim();

    public void ReplacePatterns(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var normalized = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            throw new ArgumentException("At least one pattern is required.", nameof(patterns));

        _patterns.Clear();
        _patterns.AddRange(normalized);
    }

    public bool MatchesName(string userEnteredName)
    {
        if (string.IsNullOrWhiteSpace(userEnteredName))
            return false;

        return _patterns.Any(p => userEnteredName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
