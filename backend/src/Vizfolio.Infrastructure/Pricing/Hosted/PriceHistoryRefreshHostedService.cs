using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Infrastructure.Pricing.Hosted;

/// <summary>
/// Periodically fetches daily close prices for every held holding into the local DB. Mirrors
/// <c>ExtractsRefreshHostedService</c> and shares the same <see cref="ImportRunGate"/>, so price fetches
/// never overlap an extracts import.
/// </summary>
public sealed class PriceHistoryRefreshHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportRunGate _gate;
    private readonly IOptionsMonitor<PriceHistoryOptions> _options;
    private readonly ILogger<PriceHistoryRefreshHostedService> _logger;

    public PriceHistoryRefreshHostedService(
        IServiceScopeFactory scopeFactory,
        ImportRunGate gate,
        IOptionsMonitor<PriceHistoryOptions> options,
        ILogger<PriceHistoryRefreshHostedService> logger)
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
            _logger.LogInformation("Price-history refresh schedule is disabled.");
            return;
        }

        if (schedule.StartupDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(schedule.StartupDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        if (schedule.RunOnStartup)
            await RunOnceSafelyAsync(stoppingToken);

        var interval = schedule.Interval <= TimeSpan.Zero ? TimeSpan.FromHours(24) : schedule.Interval;
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
            _logger.LogError(ex, "Scheduled price-history refresh failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_gate.TryAcquire(out var handle))
        {
            _logger.LogInformation("Skipping scheduled price-history refresh — another run is in progress.");
            return;
        }

        using (handle)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<IPriceHistoryImporter>();

            _logger.LogInformation("Beginning scheduled price-history refresh.");
            var result = await importer.ImportAsync(new PriceHistoryImportOptions(), cancellationToken);
            _logger.LogInformation(
                "Price-history refresh complete. {Upserted} rows added, {Skipped} already present, {Failed} failed.",
                result.Upserted, result.Skipped, result.Failed);
        }
    }
}
