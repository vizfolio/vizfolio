using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Pricing;

/// <summary>A key store with fixed keys, for constructing price sources in tests.</summary>
internal sealed class StaticKeyStore(params (PriceSource Provider, string Key)[] keys) : IProviderKeyStore
{
    private readonly Dictionary<PriceSource, string> _keys = keys.ToDictionary(k => k.Provider, k => k.Key);

    public Task EnsureLoadedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public string? GetApiKey(PriceSource provider) => _keys.GetValueOrDefault(provider);

    public ProviderKeySource GetKeySource(PriceSource provider)
        => _keys.ContainsKey(provider) ? ProviderKeySource.Configuration : ProviderKeySource.None;

    public Task SetApiKeyAsync(PriceSource provider, string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) _keys.Remove(provider);
        else _keys[provider] = apiKey;
        return Task.CompletedTask;
    }
}
