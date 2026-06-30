namespace Vizfolio.Domain.Reference;

public sealed class Currency
{
    private Currency() { }

    public Currency(string code, string name, int minorUnit, string? symbol)
    {
        Code = NormalizeCode(code);
        Name = ValidateName(name);
        MinorUnit = ValidateMinorUnit(minorUnit);
        Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int MinorUnit { get; private set; }

    public string? Symbol { get; private set; }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Currency code is required.", nameof(code));

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length != 3 || !trimmed.All(char.IsLetter))
            throw new ArgumentException("Currency code must be a 3-letter ISO 4217 code.", nameof(code));

        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Currency name is required.", nameof(name));
        return name.Trim();
    }

    private static int ValidateMinorUnit(int minorUnit)
    {
        if (minorUnit is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(minorUnit), "Minor unit must be between 0 and 4.");
        return minorUnit;
    }
}
