using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
            .AddStandardResilienceHandler();

        services.AddHttpClient<IFundsExtractSource, GitHubFundsExtractSource>(ConfigureClient)
            .AddStandardResilienceHandler();

        services.AddHostedService<ExtractsRefreshHostedService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubExtractOptions>>().Value;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
    }
}
