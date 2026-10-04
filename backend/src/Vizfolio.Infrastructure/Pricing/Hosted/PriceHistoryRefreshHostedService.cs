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

            var zone = ResolveTimeZone(schedule.TimeZone);
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                var next = NextRun(now, schedule, zone);
                _logger.LogInformation("Next scheduled price refresh at {Next:u}", next);
                await Task.Delay(next - now, _time, stoppingToken);
                _queue.Enqueue(PriceRefreshRequest.Everything(PriceRefreshTrigger.Schedule));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>The next scheduled refresh after <paramref name="nowUtc"/>: today's or tomorrow's DailyAt, or now + Interval.</summary>
    internal static DateTimeOffset NextRun(DateTimeOffset nowUtc, PriceSchedule schedule, TimeZoneInfo zone)
    {
        if (schedule.DailyAt is not { } dailyAt)
            return nowUtc + (schedule.Interval <= TimeSpan.Zero ? TimeSpan.FromHours(24) : schedule.Interval);

        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var candidate = local.Date + dailyAt;
        if (candidate <= local.DateTime) candidate = candidate.AddDays(1);
        return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate)).ToUniversalTime();
    }

    private TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning("Unknown time zone {TimeZone} for the price schedule; using UTC.", id);
            return TimeZoneInfo.Utc;
        }
    }
}
