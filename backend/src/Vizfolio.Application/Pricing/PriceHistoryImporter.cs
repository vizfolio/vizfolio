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
/// per distinct security/symbol, over <c>[earliest trade date, to]</c>, minus dates already stored. Prices
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
                var from = options.From ?? target.EarliestTradeDate;
                if (from > to) { continue; }

                // Only fetch the gap after what we already have (unless forced): start at the latest
                // stored date so the most recent row is refreshed in case the provider corrected it.
                var effectiveFrom = from;
                if (!options.Force && target.LatestStored is { } latest && latest >= from)
                    effectiveFrom = latest;
                if (effectiveFrom > to) { continue; }

                var request = new PriceSeriesRequest(target.QuerySymbol, target.Exchange, effectiveFrom, to);
                var source = _selector.Select(request);
                if (source is null)
                {
                    failures.Add(new ImportFailure(target.QuerySymbol, "No price source supports this symbol."));
                    continue;
                }

                var result = await source.GetDailyClosesAsync(request, cancellationToken);
                if (result is null)
                {
                    failures.Add(new ImportFailure(target.QuerySymbol, "Price source returned no series."));
                    continue;
                }

                var (added, seen) = await UpsertAsync(target, result, source.Source, cancellationToken);
                considered += result.Prices.Count;
                upserted += added;
                skipped += seen;
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
            .Select(h => new { h.AccountHoldingId, h.Symbol, h.SecurityId })
            .ToListAsync(cancellationToken);

        var tickerFilter = options.Tickers is { Count: > 0 }
            ? new HashSet<string>(options.Tickers
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToUpperInvariant()))
            : null;

        // Earliest trade date per holding drives how far back to fetch.
        var earliestByHolding = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountHoldingId != null)
            .GroupBy(t => t.AccountHoldingId!.Value)
            .Select(g => new { HoldingId = g.Key, Earliest = g.Min(t => t.TradeDate) })
            .ToDictionaryAsync(x => x.HoldingId, x => x.Earliest, cancellationToken);

        var byKey = new Dictionary<PriceSeriesKey, PriceTarget>();
        foreach (var h in holdings)
        {
            var symbol = h.Symbol!.Trim().ToUpperInvariant();
            if (tickerFilter is not null && !tickerFilter.Contains(symbol)) { continue; }

            var key = h.SecurityId is { } sid
                ? new PriceSeriesKey(PriceSeriesKind.Security, sid, null)
                : new PriceSeriesKey(PriceSeriesKind.Symbol, null, symbol);

            var earliest = earliestByHolding.TryGetValue(h.AccountHoldingId, out var e) ? e : to;

            if (byKey.TryGetValue(key, out var existing))
            {
                if (earliest < existing.EarliestTradeDate)
                    byKey[key] = existing with { EarliestTradeDate = earliest };
            }
            else
            {
                byKey[key] = new PriceTarget(
                    key.Kind, key.SecurityId, key.SymbolKey, symbol, Exchange: null, earliest, LatestStored: null);
            }
        }

        // Attach existing coverage so each series only fetches the missing tail.
        foreach (var key in byKey.Keys.ToList())
        {
            var target = byKey[key];
            var latest = await (target.Kind == PriceSeriesKind.Security
                    ? _db.PriceHistories.Where(p => p.SecurityId == target.SecurityId)
                    : _db.PriceHistories.Where(p => p.SymbolKey == target.SymbolKey))
                .Select(p => (DateOnly?)p.AsOf)
                .OrderByDescending(d => d)
                .FirstOrDefaultAsync(cancellationToken);
            byKey[key] = target with { LatestStored = latest };
        }

        return byKey.Values.ToList();
    }

    private readonly record struct PriceSeriesKey(PriceSeriesKind Kind, Guid? SecurityId, string? SymbolKey);

    private sealed record PriceTarget(
        PriceSeriesKind Kind,
        Guid? SecurityId,
        string? SymbolKey,
        string QuerySymbol,
        string? Exchange,
        DateOnly EarliestTradeDate,
        DateOnly? LatestStored);
}
