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

        // Keyless default provider. API-key providers (EODHD, Alpha Vantage) register alongside it and
        // the selector prefers them when their key is configured (higher Priority).
        services.AddHttpClient<StooqPriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<StooqPriceHistorySource>());

        services.AddHttpClient<EodhdPriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<EodhdPriceHistorySource>());

        services.AddHttpClient<AlphaVantagePriceHistorySource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IPriceHistorySource>(sp => sp.GetRequiredService<AlphaVantagePriceHistorySource>());

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
