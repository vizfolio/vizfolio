using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Pricing;

/// <summary>
/// Fetches daily close prices (and split events) for held holdings and upserts them into <c>PriceHistory</c> /
/// <c>CorporateAction</c>. Targets come from the ledger: one price series per distinct security/symbol, needed from a
/// week before the first account holding it starts trading, through <c>to</c>. Only what's missing is fetched — the gap
/// before the earliest stored close and the tail from the latest — on the raw/as-traded basis valuation needs (see
/// docs/price-history-valuation.md §5).
/// <para>
/// Each request goes down the provider fallback chain (<see cref="IPriceHistorySourceSelector.SelectAll"/>) until one
/// has raw closes; how it went is kept per series in <see cref="PriceSeriesStatus"/> (Ok, Empty = no data from any
/// provider, Failed, NoSource, AdjustedOnly) so gaps are reported rather than silent.
/// </para>
/// </summary>
public sealed class PriceHistoryImporter : IPriceHistoryImporter
{
    /// <summary>Days of prices fetched before an account's first trade, so its opening day always has a close.</summary>
    private const int LeadDays = 7;

    /// <summary>A failed series is retried no sooner than this, so a rate limit isn't hit again and again.</summary>
    internal static readonly TimeSpan FailedRetryAfter = TimeSpan.FromHours(1);

    private readonly IAppDbContext _db;
    private readonly IPriceHistorySourceSelector _selector;
    private readonly IProviderKeyStore? _keys;
    private readonly ILogger<PriceHistoryImporter> _logger;
    private readonly TimeProvider _time;

    public PriceHistoryImporter(
        IAppDbContext db,
        IPriceHistorySourceSelector selector,
        ILogger<PriceHistoryImporter> logger,
        IProviderKeyStore? keys = null,
        TimeProvider? time = null)
    {
        _db = db;
        _selector = selector;
        _logger = logger;
        _keys = keys;
        _time = time ?? TimeProvider.System;
    }

    public async Task<ImportResult> ImportAsync(
        PriceHistoryImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var to = options.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (_keys is not null) await _keys.EnsureLoadedAsync(cancellationToken);

        var targets = await BuildTargetsAsync(options, to, cancellationToken);
        if (targets.Count == 0)
        {
            stopwatch.Stop();
            return ImportResult.Empty(stopwatch.Elapsed);
        }

        var statuses = await _db.PriceSeriesStatuses.ToListAsync(cancellationToken);
        var totals = new RunTotals();
        var now = _time.GetUtcNow();
        var upToDate = 0;

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = StatusFor(target, statuses);
            if (IsUpToDate(target, status, options, now))
            {
                upToDate++;
                continue;
            }

            try
            {
                await ImportSeriesAsync(target, status, options, to, totals, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import prices for {Symbol}", target.QuerySymbol);
                status.RecordAttempt(PriceFetchOutcome.Failed, null, ex.Message, target.EarliestNeeded,
                    target.EarliestStored, target.LatestStored, now);
                totals.Failures.Add(new ImportFailure(target.QuerySymbol, ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (upToDate > 0)
            _logger.LogInformation("{Count} price series already fetched since {FreshSince:u}; skipped", upToDate, options.FreshSince);

        stopwatch.Stop();
        return new ImportResult(
            Considered: totals.Considered,
            Upserted: totals.Upserted,
            Skipped: totals.Skipped,
            Failed: totals.Failures.Count,
            Failures: totals.Failures,
            DataCleaning: Array.Empty<DataCleaningEntry>(),
            Duration: stopwatch.Elapsed);
    }

    private sealed class RunTotals
    {
        public int Considered;
        public int Upserted;
        public int Skipped;
        public List<ImportFailure> Failures { get; } = [];
    }

    /// <summary>Fetches the series' missing ranges and records how it went.</summary>
    private async Task ImportSeriesAsync(
        PriceTarget target, PriceSeriesStatus status, PriceHistoryImportOptions options, DateOnly to, RunTotals totals,
        CancellationToken cancellationToken)
    {
        var from = options.From ?? target.EarliestNeeded;
        var firstRaw = target.EarliestStored;
        var lastRaw = target.LatestStored;
        var outcomes = new List<FetchOutcome>();

        foreach (var (rangeFrom, rangeTo) in MissingRanges(from, to, target, options.Force, status.NoDataBefore))
        {
            var request = new PriceSeriesRequest(target.QuerySymbol, target.Exchange, rangeFrom, rangeTo);
            var fetch = await FetchWithFallbackAsync(request, cancellationToken);
            outcomes.Add(fetch);
            if (fetch.Kind == FetchKind.NoSource) break; // the same for every range

            if (fetch.Result is { } result && fetch.Source is { } source)
            {
                var (added, seen, addedRaw) = await UpsertAsync(target, result, source, cancellationToken);
                totals.Considered += result.Prices.Count;
                totals.Upserted += added;
                totals.Skipped += seen;
                foreach (var date in addedRaw)
                {
                    if (firstRaw is null || date < firstRaw) firstRaw = date;
                    if (lastRaw is null || date > lastRaw) lastRaw = date;
                }
            }

            // Nobody has closes this far back: remember where the history starts so it isn't asked for again.
            if (fetch.Kind == FetchKind.Empty && target.EarliestStored is { } earliest && rangeTo < earliest)
                status.MarkNoDataBefore(earliest);
        }

        var (outcome, message) = Summarize(target, outcomes, firstRaw);
        var lastSource = outcomes.LastOrDefault(o => o.Kind == FetchKind.Raw).Source ?? status.LastSource;
        status.RecordAttempt(outcome, lastSource, message, target.EarliestNeeded, firstRaw, lastRaw, _time.GetUtcNow());

        if (outcome != PriceFetchOutcome.Ok)
            totals.Failures.Add(new ImportFailure(target.QuerySymbol, message ?? outcome.ToString()));
    }

    /// <summary>
    /// True when the series needs no requests this run: it was fetched since <see cref="PriceHistoryImportOptions.FreshSince"/>
    /// (no newer close can exist) and nothing needs older history than that fetch covered. A series with no provider is
    /// never up to date — a key may have been added since — and asking costs nothing. A failed one is retried once
    /// <see cref="FailedRetryAfter"/> has passed. Forced or explicitly dated runs skip nothing.
    /// </summary>
    internal static bool IsUpToDate(PriceTarget target, PriceSeriesStatus status, PriceHistoryImportOptions options, DateTimeOffset now)
    {
        if (options.FreshSince is not { } freshSince || options.Force || options.From is not null) return false;
        if (status.LastAttemptAt is not { } attempted || attempted < freshSince) return false;
        if (status.NeededFrom is not { } coveredFrom || target.EarliestNeeded < coveredFrom) return false;

        return status.LastOutcome switch
        {
            PriceFetchOutcome.Ok or PriceFetchOutcome.Empty or PriceFetchOutcome.AdjustedOnly => true,
            PriceFetchOutcome.Failed => now - attempted < FailedRetryAfter,
            _ => false, // NoSource
        };
    }

    /// <summary>
    /// The series outcome: <c>Ok</c> once raw closes are stored and nothing failed (a range with no new data is fine —
    /// e.g. today before the close, or history that starts later than needed); otherwise why there are none.
    /// </summary>
    private static (PriceFetchOutcome, string?) Summarize(PriceTarget target, List<FetchOutcome> outcomes, DateOnly? firstRaw)
    {
        if (outcomes.Any(o => o.Kind == FetchKind.NoSource))
            return (PriceFetchOutcome.NoSource, outcomes.First(o => o.Kind == FetchKind.NoSource).Message);

        var failed = outcomes.FirstOrDefault(o => o.Kind == FetchKind.Failed);
        if (failed.Kind == FetchKind.Failed)
            return (PriceFetchOutcome.Failed, failed.Message);

        if (firstRaw is null)
        {
            if (outcomes.Any(o => o.Kind == FetchKind.Adjusted))
                return (PriceFetchOutcome.AdjustedOnly,
                    $"Only split/dividend-adjusted prices are available for {target.QuerySymbol}; they can't value as-traded shares.");
            return (PriceFetchOutcome.Empty,
                outcomes.FirstOrDefault(o => o.Kind == FetchKind.Empty).Message ?? $"No price data for {target.QuerySymbol}.");
        }

        return firstRaw > target.EarliestNeeded
            ? (PriceFetchOutcome.Ok, $"Prices start {firstRaw:yyyy-MM-dd}; needed from {target.EarliestNeeded:yyyy-MM-dd}.")
            : (PriceFetchOutcome.Ok, null);
    }

    private enum FetchKind
    {
        Raw,
        Adjusted,
        Empty,
        Failed,
        NoSource,
    }

    private readonly record struct FetchOutcome(
        FetchKind Kind, PriceSeriesResult? Result, PriceSource? Source, string? Message);

    /// <summary>
    /// Asks each provider that supports the symbol, highest priority first, until one returns raw closes. Adjusted
    /// closes are kept only if no provider has raw ones. All answering "no data" is Empty; any failure without data
    /// is Failed (worth retrying).
    /// </summary>
    private async Task<FetchOutcome> FetchWithFallbackAsync(PriceSeriesRequest request, CancellationToken cancellationToken)
    {
        var sources = _selector.SelectAll(request);
        if (sources.Count == 0)
            return new FetchOutcome(FetchKind.NoSource, null, null,
                "No price provider is set up for this symbol. Add an API key in Settings → Prices.");

        var errors = new List<string>();
        var empty = new List<string>();
        FetchOutcome? adjusted = null;
        foreach (var source in sources)
        {
            PriceSeriesResult? result;
            try
            {
                result = await source.GetDailyClosesAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Source} failed for {Symbol}; trying the next provider", source.Source, request.Symbol);
                errors.Add($"{source.DisplayName}: {ex.Message}");
                continue;
            }

            if (result is null || result.Prices.Count == 0)
            {
                empty.Add(source.DisplayName);
                continue;
            }

            if (result.Adjusted)
            {
                adjusted ??= new FetchOutcome(FetchKind.Adjusted, result, source.Source, null);
                continue;
            }

            return new FetchOutcome(FetchKind.Raw, result, source.Source, null);
        }

        if (adjusted is { } a) return a;
        if (errors.Count > 0)
            return new FetchOutcome(FetchKind.Failed, null, null, string.Join("; ", errors));
        return new FetchOutcome(FetchKind.Empty, null, null,
            $"No data from {string.Join(", ", empty)} for {request.Symbol} between {request.From:yyyy-MM-dd} and {request.To:yyyy-MM-dd}.");
    }

    /// <summary>
    /// The date ranges to fetch for a series given its stored raw closes: the gap before the earliest (unless every
    /// provider already said it has nothing older — <paramref name="noDataBefore"/>) and the tail from the latest
    /// (refetched in case the provider corrected it). Everything when forced or nothing is stored.
    /// </summary>
    internal static IEnumerable<(DateOnly From, DateOnly To)> MissingRanges(
        DateOnly from, DateOnly to, PriceTarget target, bool force, DateOnly? noDataBefore = null)
    {
        if (from > to) yield break;
        if (force || target.EarliestStored is not { } earliest || target.LatestStored is not { } latest)
        {
            yield return (from, to);
            yield break;
        }

        if (from < earliest && noDataBefore != earliest) yield return (from, earliest.AddDays(-1));
        var tailFrom = latest > from ? latest : from;
        if (tailFrom <= to) yield return (tailFrom, to);
    }

    private PriceSeriesStatus StatusFor(PriceTarget target, List<PriceSeriesStatus> statuses)
    {
        var existing = statuses.FirstOrDefault(s => s.Kind == target.Kind
                                                    && s.SecurityId == target.SecurityId
                                                    && s.SymbolKey == target.SymbolKey);
        if (existing is not null) return existing;

        var created = target.Kind == PriceSeriesKind.Security
            ? PriceSeriesStatus.ForSecurity(target.SecurityId!.Value, target.QuerySymbol)
            : PriceSeriesStatus.ForSymbol(target.SymbolKey!, target.QuerySymbol);
        statuses.Add(created);
        _db.PriceSeriesStatuses.Add(created);
        return created;
    }

    /// <summary>Adds the result's closes not already stored (raw and adjusted rows are kept apart) and its splits.</summary>
    private async Task<(int Added, int Skipped, List<DateOnly> AddedRaw)> UpsertAsync(
        PriceTarget target,
        PriceSeriesResult result,
        PriceSource source,
        CancellationToken cancellationToken)
    {
        var existingDates = await LoadExistingPriceDatesAsync(target, result.Adjusted, cancellationToken);
        var added = 0;
        var skipped = 0;
        var addedRaw = new List<DateOnly>();

        foreach (var point in result.Prices)
        {
            // Dedupe on the series key: there's no in-place update, so a stored date is left as-is.
            if (existingDates.Contains(point.AsOf)) { skipped++; continue; }

            var row = target.Kind == PriceSeriesKind.Security
                ? PriceHistory.ForSecurity(target.SecurityId!.Value, point.AsOf, point.Close, result.CurrencyCode, source, result.Adjusted)
                : PriceHistory.ForSymbol(target.SymbolKey!, point.AsOf, point.Close, result.CurrencyCode, source, result.Adjusted);
            _db.PriceHistories.Add(row);
            existingDates.Add(point.AsOf);
            added++;
            if (!result.Adjusted) addedRaw.Add(point.AsOf);
        }

        // Split events only from an as-traded series: an adjusted series has already folded them in.
        if (result.Splits.Count > 0 && !result.Adjusted)
        {
            var existingSplits = await LoadExistingSplitDatesAsync(target, cancellationToken);
            foreach (var split in result.Splits)
            {
                if (existingSplits.Contains(split.ExDate)) { continue; }

                var action = target.Kind == PriceSeriesKind.Security
                    ? CorporateAction.SplitForSecurity(target.SecurityId!.Value, split.ExDate, split.Numerator, split.Denominator, source)
                    : CorporateAction.SplitForSymbol(target.SymbolKey!, split.ExDate, split.Numerator, split.Denominator, source);
                _db.CorporateActions.Add(action);
                existingSplits.Add(split.ExDate);
            }
        }

        return (added, skipped, addedRaw);
    }

    private IQueryable<PriceHistory> StoredPrices(PriceTarget target)
        => target.Kind == PriceSeriesKind.Security
            ? _db.PriceHistories.Where(p => p.SecurityId == target.SecurityId)
            : _db.PriceHistories.Where(p => p.SymbolKey == target.SymbolKey);

    private async Task<HashSet<DateOnly>> LoadExistingPriceDatesAsync(
        PriceTarget target, bool adjusted, CancellationToken cancellationToken)
    {
        var dates = await StoredPrices(target).Where(p => p.Adjusted == adjusted).Select(p => p.AsOf).ToListAsync(cancellationToken);
        return new HashSet<DateOnly>(dates);
    }

    private async Task<HashSet<DateOnly>> LoadExistingSplitDatesAsync(
        PriceTarget target, CancellationToken cancellationToken)
    {
        var query = target.Kind == PriceSeriesKind.Security
            ? _db.CorporateActions.Where(c => c.SecurityId == target.SecurityId)
            : _db.CorporateActions.Where(c => c.SymbolKey == target.SymbolKey);

        var dates = await query.Select(c => c.ExDate).ToListAsync(cancellationToken);
        return new HashSet<DateOnly>(dates);
    }

    private async Task<List<PriceTarget>> BuildTargetsAsync(
        PriceHistoryImportOptions options, DateOnly to, CancellationToken cancellationToken)
    {
        // The account's cash ($CASH) has no market price.
        var holdings = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => h.Symbol != null && h.Kind != AccountHoldingKind.Cash)
            .Select(h => new { h.AccountHoldingId, h.AccountId, h.Symbol, h.SecurityId })
            .ToListAsync(cancellationToken);

        var tickerFilter = options.Tickers is { Count: > 0 }
            ? new HashSet<string>(options.Tickers
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToUpperInvariant()))
            : null;
        var accountFilter = options.AccountIds is { Count: > 0 } ? options.AccountIds.ToHashSet() : null;

        // How far back to fetch: from a week before the holding's *account* first trades. Positions held before
        // the imported history (derived openings) are valued from the account's start, not the holding's first row.
        var earliestByAccount = await _db.AccountTransactions
            .AsNoTracking()
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Earliest = g.Min(t => t.TradeDate) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Earliest, cancellationToken);

        var byKey = new Dictionary<PriceSeriesKey, PriceTarget>();
        var wanted = new HashSet<PriceSeriesKey>();
        foreach (var h in holdings)
        {
            var symbol = h.Symbol!.Trim().ToUpperInvariant();
            if (tickerFilter is not null && !tickerFilter.Contains(symbol)) { continue; }

            var key = h.SecurityId is { } sid
                ? new PriceSeriesKey(PriceSeriesKind.Security, sid, null)
                : new PriceSeriesKey(PriceSeriesKind.Symbol, null, symbol);
            if (accountFilter is null || accountFilter.Contains(h.AccountId)) wanted.Add(key);

            // The window spans every account holding the series, even when only some were asked for.
            var earliest = earliestByAccount.TryGetValue(h.AccountId, out var e) ? e.AddDays(-LeadDays) : to;
            if (byKey.TryGetValue(key, out var existing))
            {
                if (earliest < existing.EarliestNeeded)
                    byKey[key] = existing with { EarliestNeeded = earliest };
            }
            else
            {
                byKey[key] = new PriceTarget(
                    key.Kind, key.SecurityId, key.SymbolKey, symbol, Exchange: null, earliest, EarliestStored: null, LatestStored: null);
            }
        }

        // Attach existing raw coverage so each series only fetches what's missing at either end. Adjusted rows don't
        // count: they can't value anything, so a series that only has them still needs raw closes.
        var targets = new List<PriceTarget>();
        foreach (var key in wanted)
        {
            var target = byKey[key];
            var raw = StoredPrices(target).Where(p => !p.Adjusted);
            var earliestStored = await raw.Select(p => (DateOnly?)p.AsOf).OrderBy(d => d).FirstOrDefaultAsync(cancellationToken);
            var latestStored = await raw.Select(p => (DateOnly?)p.AsOf).OrderByDescending(d => d).FirstOrDefaultAsync(cancellationToken);
            targets.Add(target with { EarliestStored = earliestStored, LatestStored = latestStored });
        }

        return targets;
    }

    private readonly record struct PriceSeriesKey(PriceSeriesKind Kind, Guid? SecurityId, string? SymbolKey);

    internal sealed record PriceTarget(
        PriceSeriesKind Kind,
        Guid? SecurityId,
        string? SymbolKey,
        string QuerySymbol,
        string? Exchange,
        DateOnly EarliestNeeded,
        DateOnly? EarliestStored,
        DateOnly? LatestStored);
}
