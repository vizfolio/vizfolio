using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Settings;

namespace Vizfolio.Infrastructure.Pricing;

/// <summary>
/// Price provider API keys: the configured one (<c>PriceHistory:Providers:{Provider}:ApiKey</c> — env var or
/// user-secrets) wins, else the one saved from Settings as an <see cref="AppSetting"/> (<c>PriceProviders:{Provider}:ApiKey</c>).
/// Saved keys are cached in memory (a singleton), so sources can check for a key synchronously on every request.
/// </summary>
public sealed class ProviderKeyStore : IProviderKeyStore, IDisposable
{
    private readonly IOptionsMonitor<PriceHistoryOptions> _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProviderKeyStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile IReadOnlyDictionary<PriceSource, string>? _saved;

    public ProviderKeyStore(
        IOptionsMonitor<PriceHistoryOptions> options, IServiceScopeFactory scopes, ILogger<ProviderKeyStore> logger)
    {
        _options = options;
        _scopes = scopes;
        _logger = logger;
    }

    internal static string SettingKey(PriceSource provider) => $"PriceProviders:{provider}:ApiKey";

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_saved is not null) return;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_saved is not null) return;
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var rows = await db.AppSettings.AsNoTracking()
                .Where(s => s.Key.StartsWith("PriceProviders:"))
                .ToListAsync(cancellationToken);
            _saved = Enum.GetValues<PriceSource>()
                .Select(p => (Provider: p, Row: rows.FirstOrDefault(r => r.Key == SettingKey(p))))
                .Where(x => x.Row is { Value.Length: > 0 })
                .ToDictionary(x => x.Provider, x => x.Row!.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // E.g. the database isn't migrated yet: configured keys still work; saved ones load on a later call.
            _logger.LogWarning(ex, "Couldn't load saved price provider keys");
        }
        finally
        {
            _lock.Release();
        }
    }

    public string? GetApiKey(PriceSource provider)
    {
        var configured = ConfiguredKey(provider);
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return _saved?.GetValueOrDefault(provider);
    }

    public ProviderKeySource GetKeySource(PriceSource provider)
        => !string.IsNullOrWhiteSpace(ConfiguredKey(provider)) ? ProviderKeySource.Configuration
            : _saved?.ContainsKey(provider) == true ? ProviderKeySource.Settings
            : ProviderKeySource.None;

    public async Task SetApiKeyAsync(PriceSource provider, string? apiKey, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var key = apiKey?.Trim() ?? string.Empty;

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKey(provider), cancellationToken);
        if (key.Length == 0)
        {
            if (setting is not null) db.AppSettings.Remove(setting);
        }
        else if (setting is null)
        {
            db.AppSettings.Add(new AppSetting(SettingKey(provider), key));
        }
        else
        {
            setting.Update(key);
        }

        await db.SaveChangesAsync(cancellationToken);

        var saved = new Dictionary<PriceSource, string>(_saved ?? new Dictionary<PriceSource, string>());
        if (key.Length == 0) saved.Remove(provider);
        else saved[provider] = key;
        _saved = saved;
    }

    private string? ConfiguredKey(PriceSource provider)
    {
        var providers = _options.CurrentValue.Providers;
        return provider switch
        {
            PriceSource.Tiingo => providers.Tiingo.ApiKey,
            PriceSource.Eodhd => providers.Eodhd.ApiKey,
            PriceSource.AlphaVantage => providers.AlphaVantage.ApiKey,
            _ => null,
        };
    }

    public void Dispose() => _lock.Dispose();
}
