using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Pricing.Abstractions;

namespace Vizfolio.Infrastructure.Pricing.Hosted;

/// <summary>
/// The price schedule: a catch-up shortly after startup (<see cref="PriceSchedule.RunOnStartup"/>), then a daily
/// refresh after the US close (<see cref="PriceSchedule.DailyAt"/> in <see cref="PriceSchedule.TimeZone"/>), or every
/// <see cref="PriceSchedule.Interval"/>. It only enqueues: <see cref="PriceRefreshWorker"/> does the fetching.
/// </summary>
public sealed class PriceHistoryRefreshHostedService : BackgroundService
{
    private readonly IPriceRefreshQueue _queue;
    private readonly IOptionsMonitor<PriceHistoryOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PriceHistoryRefreshHostedService> _logger;

    public PriceHistoryRefreshHostedService(
        IPriceRefreshQueue queue,
        IOptionsMonitor<PriceHistoryOptions> options,
        ILogger<PriceHistoryRefreshHostedService> logger,
        TimeProvider? time = null)
    {
        _queue = queue;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = _options.CurrentValue.Schedule;
        if (!schedule.Enabled)
        {
            _logger.LogInformation("Price-history refresh schedule is disabled.");
            return;
        }

        try
        {
            if (schedule.StartupDelay > TimeSpan.Zero)
                await Task.Delay(schedule.StartupDelay, _time, stoppingToken);

            if (schedule.RunOnStartup)
                _queue.Enqueue(PriceRefreshRequest.Everything(PriceRefreshTrigger.Schedule));

            var zone = PriceScheduleClock.ResolveTimeZone(schedule.TimeZone);
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                var next = PriceScheduleClock.NextRun(now, schedule, zone);
                _logger.LogInformation("Next scheduled price refresh at {Next:u}", next);
                await Task.Delay(next - now, _time, stoppingToken);
                _queue.Enqueue(PriceRefreshRequest.Everything(PriceRefreshTrigger.Schedule));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
