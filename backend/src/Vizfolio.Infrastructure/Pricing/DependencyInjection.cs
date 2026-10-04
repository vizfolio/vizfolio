using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Infrastructure.Pricing.Hosted;
using Vizfolio.Infrastructure.Pricing.Sources;

namespace Vizfolio.Infrastructure.Pricing;

public static class DependencyInjection
{
    public static IServiceCollection AddPricing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PriceHistoryOptions>()
            .Bind(configuration.GetSection(PriceHistoryOptions.SectionName));

        // API keys: configuration first, then the key saved from Settings (a singleton cache over AppSetting).
        services.AddSingleton<ProviderKeyStore>();
        services.AddSingleton<IProviderKeyStore>(sp => sp.GetRequiredService<ProviderKeyStore>());

        // Providers, tried highest priority first (Tiingo, Alpha Vantage, EODHD), falling back down the chain when one
        // fails or has no data. Keyless Stooq is last and off by default: its closes are adjusted.
        services.AddHttpClient<StooqPriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<StooqPriceHistorySource>());

        services.AddHttpClient<EodhdPriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<EodhdPriceHistorySource>());

        services.AddHttpClient<AlphaVantagePriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<AlphaVantagePriceHistorySource>());

        services.AddHttpClient<TiingoPriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<TiingoPriceHistorySource>());

        // Background fetching: imports, the schedule and Settings enqueue; one worker runs them.
        services.AddSingleton<PriceRefreshQueue>();
        services.AddSingleton<IPriceRefreshQueue>(sp => sp.GetRequiredService<PriceRefreshQueue>());
        services.AddSingleton<IPriceRefreshStatus>(sp => sp.GetRequiredService<PriceRefreshQueue>());
        services.AddHostedService<PriceRefreshWorker>();
        services.AddHostedService<PriceHistoryRefreshHostedService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<PriceHistoryOptions>>().Value;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        client.Timeout = TimeSpan.FromMinutes(5);
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
    }
}
