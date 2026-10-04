using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.PortfolioImports.Services;

namespace Vizfolio.Infrastructure.PortfolioImports;

/// <summary>
/// One-off upkeep of imported data at startup: rewrites synthetic transaction ids to their stable form
/// (<see cref="IStableExternalIdMigration"/>). Idempotent, so it runs every time; a failure is logged, never fatal.
/// </summary>
public sealed class ImportMaintenanceHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportMaintenanceHostedService> _logger;

    public ImportMaintenanceHostedService(IServiceScopeFactory scopeFactory, ILogger<ImportMaintenanceHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStableExternalIdMigration>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import maintenance at startup failed; imported data is unchanged.");
        }
    }
}
