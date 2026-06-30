namespace Vizfolio.Domain.Reference;

public sealed class AssetClass
{
    private AssetClass() { }

    public AssetClass(string code, string name, string? description)
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
            throw new ArgumentException("Asset class code is required.", nameof(code));

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length is < 1 or > 20 || !trimmed.All(c => char.IsLetter(c) || c == '_'))
            throw new ArgumentException("Asset class code must be 1-20 characters (letters or '_').", nameof(code));

        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Asset class name is required.", nameof(name));
        return name.Trim();
    }
}
