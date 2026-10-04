using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Infrastructure.Pricing.Hosted;

/// <summary>
/// Runs queued price refreshes one at a time. Each run <i>waits</i> for the shared <see cref="ImportRunGate"/> (it
/// never overlaps an extracts import, and is never skipped because one is running).
/// </summary>
public sealed class PriceRefreshWorker : BackgroundService
{
    private readonly PriceRefreshQueue _queue;
    private readonly ImportRunGate _gate;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PriceRefreshWorker> _logger;

    public PriceRefreshWorker(
        PriceRefreshQueue queue, ImportRunGate gate, IServiceScopeFactory scopes, ILogger<PriceRefreshWorker> logger)
    {
        _queue = queue;
        _gate = gate;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                using var handle = await _gate.AcquireAsync(stoppingToken);
                if (_queue.StartNext() is not { } request) continue;
                await RunAsync(request, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunAsync(PriceRefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<IPriceHistoryImporter>();
            _logger.LogInformation(
                "Fetching prices ({Trigger}) for {Scope}", request.Trigger,
                request.AccountIds is null ? "all holdings" : $"{request.AccountIds.Count} account(s)");

            var result = await importer.ImportAsync(new PriceHistoryImportOptions(AccountIds: request.AccountIds), cancellationToken);
            _queue.Finished(result, null);
            _logger.LogInformation(
                "Price fetch complete: {Upserted} closes added, {Failed} series without usable prices.",
                result.Upserted, result.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _queue.Finished(null, "Stopped.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background price fetch failed.");
            _queue.Finished(null, ex.Message);
        }
    }
}
