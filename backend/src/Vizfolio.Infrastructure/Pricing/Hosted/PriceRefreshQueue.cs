using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.Pricing.Abstractions;

namespace Vizfolio.Infrastructure.Pricing.Hosted;

/// <summary>
/// The background price-fetch queue (a singleton): <see cref="Enqueue"/> records what's pending and hands the request
/// to <see cref="PriceRefreshWorker"/>, which takes everything queued at once as one run. Tracks which accounts are
/// waiting on prices so valuations can say "prices updating" rather than "incomplete".
/// </summary>
public sealed class PriceRefreshQueue : IPriceRefreshQueue, IPriceRefreshStatus
{
    private readonly Channel<PriceRefreshRequest> _channel = Channel.CreateUnbounded<PriceRefreshRequest>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly IOptionsMonitor<PriceHistoryOptions> _options;
    private readonly object _lock = new();

    private readonly HashSet<Guid> _queued = [];
    private bool _queuedAll;
    private HashSet<Guid> _running = [];
    private bool _runningAll;
    private bool _isRunning;
    private DateTimeOffset? _lastStartedAt;
    private DateTimeOffset? _lastFinishedAt;
    private PriceRefreshTrigger? _lastTrigger;
    private int? _lastUpserted;
    private int? _lastFailed;
    private string? _lastError;

    public PriceRefreshQueue(IOptionsMonitor<PriceHistoryOptions> options)
    {
        _options = options;
    }

    public void Enqueue(PriceRefreshRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Trigger == PriceRefreshTrigger.Import && !_options.CurrentValue.RefreshOnImport) return;
        if (request.AccountIds is { Count: 0 }) return;

        lock (_lock)
        {
            if (request.AccountIds is null) _queuedAll = true;
            else _queued.UnionWith(request.AccountIds);
            _channel.Writer.TryWrite(request);
        }
    }

    public bool IsPending(Guid accountId)
    {
        lock (_lock)
            return _queuedAll || _runningAll || _queued.Contains(accountId) || _running.Contains(accountId);
    }

    public PriceRefreshState Current
    {
        get
        {
            lock (_lock)
                return new PriceRefreshState(
                    _isRunning, _queuedAll || _queued.Count > 0, _lastStartedAt, _lastFinishedAt, _lastTrigger?.ToString(),
                    _lastUpserted, _lastFailed, _lastError);
        }
    }

    internal ChannelReader<PriceRefreshRequest> Reader => _channel.Reader;

    /// <summary>Takes everything queued as one run: every series when any request asked for all, else the union of accounts.</summary>
    internal PriceRefreshRequest? StartNext()
    {
        lock (_lock)
        {
            var requests = new List<PriceRefreshRequest>();
            while (_channel.Reader.TryRead(out var request)) requests.Add(request);
            if (requests.Count == 0) return null;

            _running = [.. _queued];
            _runningAll = _queuedAll;
            _queued.Clear();
            _queuedAll = false;
            _isRunning = true;
            _lastStartedAt = DateTimeOffset.UtcNow;

            // The most deliberate trigger names the run (Manual > Schedule > Import).
            _lastTrigger = requests.Max(r => r.Trigger);
            return _runningAll
                ? PriceRefreshRequest.Everything(_lastTrigger.Value)
                : PriceRefreshRequest.ForAccounts(_running, _lastTrigger.Value);
        }
    }

    internal void Finished(ImportResult? result, string? error)
    {
        lock (_lock)
        {
            _running = [];
            _runningAll = false;
            _isRunning = false;
            _lastFinishedAt = DateTimeOffset.UtcNow;
            _lastUpserted = result?.Upserted;
            _lastFailed = result?.Failed;
            _lastError = error;
        }
    }
}
