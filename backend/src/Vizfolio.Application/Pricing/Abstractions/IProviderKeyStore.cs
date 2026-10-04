using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Pricing.Abstractions;

/// <summary>
/// API keys for price providers. Configuration (env var or user-secrets) wins; otherwise the key saved from Settings
/// (an <c>AppSetting</c> row). Keys are write-only from the API: they're never returned. The seam for a multi-user
/// version, which would keep per-user keys encrypted (roadmap Appendix A.12).
/// </summary>
public interface IProviderKeyStore
{
    /// <summary>Loads saved keys once; call before <see cref="GetApiKey"/> in a fresh process.</summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    string? GetApiKey(PriceSource provider);

    ProviderKeySource GetKeySource(PriceSource provider);

    /// <summary>Saves (or, with a blank key, removes) the key kept in settings.</summary>
    Task SetApiKeyAsync(PriceSource provider, string? apiKey, CancellationToken cancellationToken);
}

public enum ProviderKeySource
{
    None,
    Configuration,
    Settings,
}
