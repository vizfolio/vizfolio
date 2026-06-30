namespace Vizfolio.Domain.Reference;

public sealed class Country
{
    private Country() { }

    public Country(string code, string name, string? region)
    {
        Code = NormalizeCode(code);
        Name = ValidateName(name);
        Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Region { get; private set; }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Country code is required.", nameof(code));

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length != 2 || !trimmed.All(char.IsLetter))
            throw new ArgumentException("Country code must be a 2-letter ISO 3166-1 alpha-2 code.", nameof(code));

        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Country name is required.", nameof(name));
        return name.Trim();
    }
}
