using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Application.Extracts.Importers;

/// <summary>
/// Upserts every money market fund from the registry by series ID — which tickers are money market funds, and
/// whether each keeps a stable price (and what it is). A fund that drops out of the registry (e.g. liquidated) keeps
/// its last row, so older ledgers that hold it still value correctly. A registry not yet published is a no-op.
/// </summary>
public sealed class MoneyMarketFundsImporter : IMoneyMarketFundsImporter
{
    private readonly IAppDbContext _db;
    private readonly IFundsExtractSource _source;
    private readonly ILogger<MoneyMarketFundsImporter> _logger;

    public MoneyMarketFundsImporter(
        IAppDbContext db,
        IFundsExtractSource source,
        ILogger<MoneyMarketFundsImporter> logger)
    {
        _db = db;
        _source = source;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var registry = await _source.GetMoneyMarketRegistryAsync(cancellationToken);
        if (registry is null || registry.Funds.Count == 0)
        {
            stopwatch.Stop();
            return ImportResult.Empty(stopwatch.Elapsed);
        }

        var existing = await _db.MoneyMarketFunds.ToDictionaryAsync(f => f.SeriesId, StringComparer.Ordinal, cancellationToken);
        var upserted = 0;
        var skipped = 0;
        var failures = new List<ImportFailure>();

        foreach (var extract in registry.Funds)
        {
            try
            {
                var seriesId = extract.SeriesId.Trim().ToUpperInvariant();
                if (existing.TryGetValue(seriesId, out var fund)
                    && fund.SourceFiling == extract.SourceFiling.Trim())
                {
                    skipped++;
                    continue;
                }

                if (fund is null)
                {
                    fund = new MoneyMarketFund(seriesId);
                    _db.MoneyMarketFunds.Add(fund);
                    existing[seriesId] = fund;
                }

                fund.Update(
                    extract.Name,
                    extract.RegistrantCik,
                    extract.Category,
                    extract.SeeksStablePrice,
                    extract.StablePricePerShare,
                    extract.IsRetail,
                    extract.AsOf,
                    extract.SourceFiling,
                    (extract.Classes ?? []).Select(c => c.Ticker).OfType<string>());
                upserted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to import money market fund {SeriesId}", extract.SeriesId);
                failures.Add(new ImportFailure(extract.SeriesId, ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        stopwatch.Stop();
        _logger.LogInformation(
            "Money market registry: {Upserted} upserted, {Skipped} unchanged, {Failed} failed", upserted, skipped, failures.Count);

        return new ImportResult(
            Considered: registry.Funds.Count,
            Upserted: upserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures,
            DataCleaning: [],
            Duration: stopwatch.Elapsed);
    }
}
