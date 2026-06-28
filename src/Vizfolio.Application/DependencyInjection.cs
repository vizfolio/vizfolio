using Microsoft.Extensions.DependencyInjection;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Importers;

namespace Vizfolio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISecuritiesImporter, SecuritiesImporter>();
        services.AddScoped<IFundsImporter, FundsImporter>();
        services.AddScoped<IHoldingRelinker, HoldingRelinker>();
        return services;
    }
}
