namespace Vizfolio.Domain.Settings;

/// <summary>
/// A server-side setting changed from the UI (e.g. a price provider's API key), as a key/value pair. Values are plain
/// text: acceptable for a single self-hosted user; a multi-user deployment moves secrets behind a per-user encrypted
/// store (roadmap Appendix A.12). Configuration (env vars, user-secrets) always wins over a stored value.
/// </summary>
public sealed class AppSetting
{
    private AppSetting() { }

    public AppSetting(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));

        Key = key.Trim();
        Update(value);
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string value)
    {
        Value = value ?? string.Empty;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
