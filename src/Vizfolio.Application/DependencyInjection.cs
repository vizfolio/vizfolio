using Microsoft.Extensions.DependencyInjection;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Importers;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;

namespace Vizfolio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISecuritiesImporter, SecuritiesImporter>();
        services.AddScoped<IFundsImporter, FundsImporter>();
        services.AddScoped<IHoldingRelinker, HoldingRelinker>();

        services.AddScoped<IPortfolioFileParser, QfxFileParser>();
        services.AddScoped<IPortfolioFileParser, CsvFileParser>();
        services.AddScoped<IPortfolioImportService, PortfolioImportService>();
        services.AddScoped<ILedgerRelinker, LedgerRelinker>();
        return services;
    }
}
