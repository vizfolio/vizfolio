using Microsoft.Extensions.DependencyInjection;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Importers;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Application.Pricing;
using Vizfolio.Application.Pricing.Abstractions;

namespace Vizfolio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ISecuritiesImporter, SecuritiesImporter>();
        services.AddScoped<IFundsImporter, FundsImporter>();
        services.AddScoped<IMoneyMarketFundsImporter, MoneyMarketFundsImporter>();
        services.AddScoped<IHoldingRelinker, HoldingRelinker>();

        // Import parsers are plug-ins: register an IPortfolioFileParser and the pipeline discovers it.
        // Auto-detect offers them highest Priority first, so provider-specific parsers sit above generics.
        services.AddScoped<IPortfolioFileParser, QfxFileParser>();
        services.AddScoped<IPortfolioFileParser, VanguardTransactionHistoryReportParser>();
        services.AddScoped<IPortfolioImportService, PortfolioImportService>();
        services.AddScoped<ILedgerRelinker, LedgerRelinker>();
        // Valuation rules shared by performance and the Holdings view. Defaults here; the host may replace the
        // options with configuration-bound values (see Program.cs, section "Valuation").
        services.AddSingleton(new ValuationOptions());
        services.AddScoped<AccountValuationLoader>();
        services.AddScoped<IPortfolioPerformanceService, PortfolioPerformanceService>();
        services.AddScoped<IAccountHistoryService, AccountHistoryService>();
        services.AddScoped<IImpliedContributionService, ImpliedContributionService>();

        // Return-metric strategies. Swap the TWRR line to
        // ChainedSubPeriodTimeWeightedReturnCalculator to opt into the strict GIPS-style
        // calculator (returns null until multiple snapshots exist across the period).
        services.AddScoped<ITimeWeightedReturnCalculator, ModifiedDietzTimeWeightedReturnCalculator>();
        services.AddScoped<IMoneyWeightedReturnCalculator, XirrMoneyWeightedReturnCalculator>();

        // Price-history pipeline. Sources (IPriceHistorySource) are registered in the Infrastructure
        // layer (they need HttpClients); the selector dispatches to the highest-priority one that
        // supports a request, mirroring the parser plug-in model above.
        services.AddScoped<IPriceHistorySourceSelector, PriceHistorySourceSelector>();
        services.AddScoped<IPriceHistoryImporter, PriceHistoryImporter>();
        return services;
    }
}
