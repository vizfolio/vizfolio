namespace Vizfolio.Domain.Reference;

public sealed class AssetCategory
{
    private AssetCategory() { }

    public AssetCategory(string code, string name, string? description)
    {
        Code = NormalizeCode(code);
        Name = ValidateName(name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Asset category code is required.", nameof(code));

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length is < 1 or > 10 || !trimmed.All(c => char.IsLetterOrDigit(c) || c == '-'))
            throw new ArgumentException("Asset category code must be 1-10 characters (letters, digits, or '-').", nameof(code));

        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Asset category name is required.", nameof(name));
        return name.Trim();
    }
}
