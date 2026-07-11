using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Infrastructure.Extracts.Hosted;
using Vizfolio.Infrastructure.Extracts.Sources;

namespace Vizfolio.Infrastructure.Extracts;

public static class DependencyInjection
{
    public static IServiceCollection AddExtracts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GitHubExtractOptions>()
            .Bind(configuration.GetSection(GitHubExtractOptions.SectionName));

        services.AddSingleton<ImportRunGate>();

        services.AddHttpClient<ISecuritiesExtractSource, GitHubSecuritiesExtractSource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<GitHubFundsExtractSource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddHttpClient<PerFileFundsExtractSource>(ConfigureClient)
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<IFundsExtractSource>(sp =>
        {
            var mode = sp.GetRequiredService<IOptions<GitHubExtractOptions>>().Value.Sources.Funds.Mode;
            return mode == FundsSourceMode.PerFile
                ? sp.GetRequiredService<PerFileFundsExtractSource>()
                : sp.GetRequiredService<GitHubFundsExtractSource>();
        });

        services.AddHostedService<ExtractsRefreshHostedService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubExtractOptions>>().Value;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        client.Timeout = TimeSpan.FromMinutes(10);
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(10);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
    }
}
