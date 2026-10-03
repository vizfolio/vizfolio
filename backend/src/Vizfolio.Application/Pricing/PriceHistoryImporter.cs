using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Pricing;

/// <summary>
/// Fetches daily close prices (and split events) for every held holding in the database and upserts them
/// into <c>PriceHistory</c> / <c>CorporateAction</c>. Targets are derived from the ledger: one price series
/// per distinct security/symbol, from a week before its account's first trade to <c>to</c>, minus dates already stored. Prices
/// are stored on the raw/as-traded basis to match the ledger — see docs/price-history-valuation.md §5.
/// </summary>
public sealed class PriceHistoryImporter : IPriceHistoryImporter
{
    private readonly IAppDbContext _db;
    private readonly IPriceHistorySourceSelector _selector;
    private readonly ILogger<PriceHistoryImporter> _logger;

    public PriceHistoryImporter(
        IAppDbContext db,
        IPriceHistorySourceSelector selector,
        ILogger<PriceHistoryImporter> logger)
    {
        _db = db;
        _selector = selector;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(
        PriceHistoryImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var to = options.To ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var targets = await BuildTargetsAsync(options, to, cancellationToken);
        if (targets.Count == 0)
        {
            stopwatch.Stop();
            return ImportResult.Empty(stopwatch.Elapsed);
        }

        var considered = 0;
        var upserted = 0;
        var skipped = 0;
        var failures = new List<ImportFailure>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var from = options.From ?? target.EarliestNeeded;
                if (from > to) { continue; }

                // Fetch only what's missing (unless forced): the gap before the earliest stored close — e.g. older
                // history imported later, or positions held before the imported history — and the tail from the
                // latest stored close (refetched in case the provider corrected it).
                foreach (var (rangeFrom, rangeTo) in MissingRanges(from, to, target, options.Force))
                {
                    var request = new PriceSeriesRequest(target.QuerySymbol, target.Exchange, rangeFrom, rangeTo);
                    var source = _selector.Select(request);
                    if (source is null)
                    {
                        failures.Add(new ImportFailure(target.QuerySymbol, "No price source supports this symbol."));
                        break;
                    }

                    var result = await source.GetDailyClosesAsync(request, cancellationToken);
                    if (result is null)
                    {
                        failures.Add(new ImportFailure(target.QuerySymbol, "Price source returned no series."));
                        break;
                    }

                    var (added, seen) = await UpsertAsync(target, result, source.Source, cancellationToken);
                    considered += result.Prices.Count;
                    upserted += added;
                    skipped += seen;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import prices for {Symbol}", target.QuerySymbol);
                failures.Add(new ImportFailure(target.QuerySymbol, ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        return new ImportResult(
            Considered: considered,
            Upserted: upserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures,
            DataCleaning: Array.Empty<DataCleaningEntry>(),
            Duration: stopwatch.Elapsed);
    }

    /// <summary>The date ranges to fetch for a series given what's already stored.</summary>
    internal static IEnumerable<(DateOnly From, DateOnly To)> MissingRanges(
        DateOnly from, DateOnly to, PriceTarget target, bool force)
    {
        if (force || target.EarliestStored is not { } earliest || target.LatestStored is not { } latest)
        {
            yield return (from, to);
            yield break;
        }

        if (from < earliest) yield return (from, earliest.AddDays(-1));
        var tailFrom = latest > from ? latest : from;
        if (tailFrom <= to) yield return (tailFrom, to);
    }

    private async Task<(int Added, int Skipped)> UpsertAsync(
        PriceTarget target,
        PriceSeriesResult result,
        PriceSource source,
        CancellationToken cancellationToken)
    {
        var existingDates = await LoadExistingPriceDatesAsync(target, cancellationToken);
        var added = 0;
        var skipped = 0;

        foreach (var point in result.Prices)
        {
            // Dedupe on the series key: there's no in-place update, so a stored date is left as-is.
            // Force only widens the fetch window (see BuildTargetsAsync); it never re-inserts a row.
            if (existingDates.Contains(point.AsOf)) { skipped++; continue; }

            var row = target.Kind == PriceSeriesKind.Security
                ? PriceHistory.ForSecurity(target.SecurityId!.Value, point.AsOf, point.Close, result.CurrencyCode, source)
                : PriceHistory.ForSymbol(target.SymbolKey!, point.AsOf, point.Close, result.CurrencyCode, source);
            _db.PriceHistories.Add(row);
            existingDates.Add(point.AsOf);
            added++;
        }

        if (result.Splits.Count > 0)
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

        return (added, skipped);
    }

    private async Task<HashSet<DateOnly>> LoadExistingPriceDatesAsync(
        PriceTarget target, CancellationToken cancellationToken)
    {
        var query = target.Kind == PriceSeriesKind.Security
            ? _db.PriceHistories.Where(p => p.SecurityId == target.SecurityId)
            : _db.PriceHistories.Where(p => p.SymbolKey == target.SymbolKey);

        var dates = await query.Select(p => p.AsOf).ToListAsync(cancellationToken);
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
        var holdings = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => h.Symbol != null)
            .Select(h => new { h.AccountHoldingId, h.AccountId, h.Symbol, h.SecurityId })
            .ToListAsync(cancellationToken);

        var tickerFilter = options.Tickers is { Count: > 0 }
            ? new HashSet<string>(options.Tickers
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToUpperInvariant()))
            : null;

        // How far back to fetch: from a week before the holding's *account* first trades. Positions held before
        // the imported history (derived openings) are valued from the account's start, not the holding's first row.
        var earliestByAccount = await _db.AccountTransactions
            .AsNoTracking()
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Earliest = g.Min(t => t.TradeDate) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Earliest, cancellationToken);

        var byKey = new Dictionary<PriceSeriesKey, PriceTarget>();
        foreach (var h in holdings)
        {
            var symbol = h.Symbol!.Trim().ToUpperInvariant();
            if (tickerFilter is not null && !tickerFilter.Contains(symbol)) { continue; }

            var key = h.SecurityId is { } sid
                ? new PriceSeriesKey(PriceSeriesKind.Security, sid, null)
                : new PriceSeriesKey(PriceSeriesKind.Symbol, null, symbol);

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

        // Attach existing coverage so each series only fetches what's missing at either end.
        foreach (var key in byKey.Keys.ToList())
        {
            var target = byKey[key];
            var stored = target.Kind == PriceSeriesKind.Security
                ? _db.PriceHistories.Where(p => p.SecurityId == target.SecurityId)
                : _db.PriceHistories.Where(p => p.SymbolKey == target.SymbolKey);
            var earliestStored = await stored.Select(p => (DateOnly?)p.AsOf).OrderBy(d => d).FirstOrDefaultAsync(cancellationToken);
            var latestStored = await stored.Select(p => (DateOnly?)p.AsOf).OrderByDescending(d => d).FirstOrDefaultAsync(cancellationToken);
            byKey[key] = target with { EarliestStored = earliestStored, LatestStored = latestStored };
        }

        return byKey.Values.ToList();
    }

    private readonly record struct PriceSeriesKey(PriceSeriesKind Kind, Guid? SecurityId, string? SymbolKey);

    /// <summary>Days of prices fetched before an account's first trade, so its opening day always has a close.</summary>
    private const int LeadDays = 7;

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
