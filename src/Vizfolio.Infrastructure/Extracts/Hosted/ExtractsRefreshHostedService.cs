using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Infrastructure.Extracts.Hosted;

public sealed class ExtractsRefreshHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportRunGate _gate;
    private readonly IOptionsMonitor<GitHubExtractOptions> _options;
    private readonly ILogger<ExtractsRefreshHostedService> _logger;

    public ExtractsRefreshHostedService(
        IServiceScopeFactory scopeFactory,
        ImportRunGate gate,
        IOptionsMonitor<GitHubExtractOptions> options,
        ILogger<ExtractsRefreshHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _gate = gate;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = _options.CurrentValue.Schedule;
        if (!schedule.Enabled)
        {
            _logger.LogInformation("Extracts refresh schedule is disabled.");
            return;
        }

        if (schedule.StartupDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(schedule.StartupDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        if (schedule.RunOnStartup)
            await RunOnceSafelyAsync(stoppingToken);

        var interval = schedule.Interval <= TimeSpan.Zero ? TimeSpan.FromHours(6) : schedule.Interval;
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceSafelyAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunOnceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled extracts refresh failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_gate.TryAcquire(out var handle))
        {
            _logger.LogInformation("Skipping scheduled extracts refresh — another run is in progress.");
            return;
        }

        using (handle)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var securities = scope.ServiceProvider.GetRequiredService<ISecuritiesImporter>();
            var funds = scope.ServiceProvider.GetRequiredService<IFundsImporter>();
            var relinker = scope.ServiceProvider.GetRequiredService<IHoldingRelinker>();

            _logger.LogInformation("Beginning scheduled extracts refresh.");
            var secResult = await securities.ImportAsync(new SecuritiesImportOptions(), cancellationToken);
            var fundResult = await funds.ImportAsync(new FundsImportOptions(), cancellationToken);
            var relinked = await relinker.RelinkAsync(cancellationToken);

            _logger.LogInformation(
                "Extracts refresh complete. Securities: {SecUp}/{SecTotal} upserted. Funds: {FundUp}/{FundTotal} upserted. Re-linked: {Relinked}.",
                secResult.Upserted, secResult.Considered, fundResult.Upserted, fundResult.Considered, relinked);
        }
    }
}
