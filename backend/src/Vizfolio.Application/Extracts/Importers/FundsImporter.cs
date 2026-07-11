using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Application.Extracts.Importers;

public sealed class FundsImporter : IFundsImporter
{
    private readonly IAppDbContext _db;
    private readonly IFundsExtractSource _source;
    private readonly ILogger<FundsImporter> _logger;

    public FundsImporter(
        IAppDbContext db,
        IFundsExtractSource source,
        ILogger<FundsImporter> logger)
    {
        _db = db;
        _source = source;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(FundsImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var manifest = await _source.GetManifestAsync(cancellationToken);

        var entries = FilterEntries(manifest, options.SeriesIds);
        if (entries.Count == 0)
        {
            stopwatch.Stop();
            return ImportResult.Empty(stopwatch.Elapsed);
        }

        var gate = await ReferenceCodeGate.LoadAsync(_db, cancellationToken);

        var seriesIds = entries.Select(e => e.SeriesId).ToList();
        var existingFunds = await _db.Funds
            .Where(f => seriesIds.Contains(f.SeriesId))
            .ToDictionaryAsync(f => f.SeriesId, cancellationToken);

        var latestAsOfByFund = await _db.FundSnapshots
            .Where(s => existingFunds.Values.Select(f => f.FundId).Contains(s.FundId))
            .GroupBy(s => s.FundId)
            .Select(g => new { FundId = g.Key, MaxAsOf = g.Max(x => x.AsOf) })
            .ToDictionaryAsync(x => x.FundId, x => x.MaxAsOf, cancellationToken);

        var upserted = 0;
        var skipped = 0;
        var failures = new List<ImportFailure>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!TryParsePeriod(entry.LatestPeriod, out var manifestAsOf))
                {
                    failures.Add(new ImportFailure(entry.SeriesId, $"Unrecognized latest_period '{entry.LatestPeriod}'."));
                    continue;
                }

                existingFunds.TryGetValue(entry.SeriesId, out var fund);
                var alreadyImported = fund is not null
                    && latestAsOfByFund.TryGetValue(fund.FundId, out var maxAsOf)
                    && maxAsOf >= manifestAsOf;

                if (alreadyImported && !options.Force)
                {
                    skipped++;
                    continue;
                }

                var snapshot = await _source.GetSnapshotAsync(entry.SeriesId, entry.LatestPeriod, cancellationToken);
                if (snapshot is null)
                {
                    failures.Add(new ImportFailure(entry.SeriesId, "Fund snapshot not found."));
                    continue;
                }

                fund ??= CreateFund(entry, snapshot);
                fund.UpdateProfile(
                    snapshot.Fund.Name ?? entry.Name,
                    snapshot.Fund.RegistrantCik ?? entry.RegistrantCik,
                    snapshot.Fund.RegistrantName);

                if (existingFunds.TryAdd(entry.SeriesId, fund))
                    _db.Funds.Add(fund);

                await UpsertSnapshotAsync(fund, snapshot, gate, options.Force, cancellationToken);
                upserted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import fund {SeriesId}", entry.SeriesId);
                failures.Add(new ImportFailure(entry.SeriesId, ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        gate.LogReport(_logger);

        stopwatch.Stop();
        return new ImportResult(
            Considered: entries.Count,
            Upserted: upserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures,
            DataCleaning: gate.BuildReport(),
            Duration: stopwatch.Elapsed);
    }

    private async Task UpsertSnapshotAsync(Fund fund, FundSnapshotExtract snapshot, ReferenceCodeGate gate, bool force, CancellationToken cancellationToken)
    {
        var existing = await _db.FundSnapshots
            .FirstOrDefaultAsync(s => s.FundId == fund.FundId && s.AsOf == snapshot.Fund.AsOf, cancellationToken);

        if (existing is not null)
        {
            if (!force) return;
            var oldHoldings = await _db.FundHoldings
                .Where(h => h.FundSnapshotId == existing.FundSnapshotId)
                .ToListAsync(cancellationToken);
            _db.FundHoldings.RemoveRange(oldHoldings);
            _db.FundSnapshots.Remove(existing);
        }

        var newSnapshot = new FundSnapshot(
            fund.FundId,
            snapshot.Fund.AsOf,
            snapshot.Fund.SourceFiling,
            snapshot.Fund.SourceUrl);

        newSnapshot.SetFinancials(
            snapshot.Fund.NetAssetsUsd,
            snapshot.Fund.TotalAssetsUsd,
            snapshot.Fund.TotalLiabilitiesUsd,
            snapshot.Fund.CashNotInPortfolioUsd);

        if (snapshot.Fund.IsFinalFiling)
            newSnapshot.MarkAsFinalFiling();

        newSnapshot.ReplaceShareClasses((snapshot.Fund.ShareClasses ?? [])
            .Select(sc => new ShareClass(sc.ClassId, sc.Name, sc.Ticker, sc.ExpenseRatio)));

        newSnapshot.ReplaceMonthlyReturns((snapshot.Fund.MonthlyReturns ?? [])
            .Select(mr => new MonthlyReturn(mr.Month, mr.ReturnPct, mr.ClassId)));

        _db.FundSnapshots.Add(newSnapshot);

        await AddHoldingsAsync(newSnapshot, snapshot.Holdings ?? [], gate, cancellationToken);
    }

    private async Task AddHoldingsAsync(FundSnapshot snapshot, IReadOnlyList<HoldingExtract> holdings, ReferenceCodeGate gate, CancellationToken cancellationToken)
    {
        if (holdings.Count == 0) return;

        var issuerCiks = holdings
            .Select(h => h.IssuerCik)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => NormalizeCik(c!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var securityIdByCik = issuerCiks.Count == 0
            ? new Dictionary<string, Guid>(StringComparer.Ordinal)
            : await _db.Securities
                .Where(s => issuerCiks.Contains(s.Cik))
                .ToDictionaryAsync(s => s.Cik, s => s.SecurityId, cancellationToken);

        foreach (var h in holdings)
        {
            var holding = new FundHolding(snapshot.FundSnapshotId, h.Weight);
            holding.SetIdentifiers(h.Name, h.Ticker, h.Isin);
            holding.SetValuation(h.FairValueUsd, h.Balance, units: null);
            holding.SetClassification(
                gate.AcceptAssetCategory(h.AssetCategory),
                gate.AcceptAssetClass(h.AssetClass),
                gate.AcceptCountry(h.Country),
                gate.AcceptCurrency(h.Currency));
            holding.SetIssuerCik(h.IssuerCik);

            if (holding.IssuerCik is not null && securityIdByCik.TryGetValue(holding.IssuerCik, out var securityId))
                holding.LinkToSecurity(securityId);

            _db.FundHoldings.Add(holding);
        }
    }

    private static Fund CreateFund(FundsManifestEntry entry, FundSnapshotExtract snapshot)
        => new(snapshot.Fund.SeriesId ?? entry.SeriesId);

    private static List<FundsManifestEntry> FilterEntries(FundsManifest manifest, IReadOnlyList<string>? seriesIdFilter)
    {
        if (seriesIdFilter is null || seriesIdFilter.Count == 0)
            return manifest.Funds.ToList();

        var filter = new HashSet<string>(seriesIdFilter
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()), StringComparer.Ordinal);

        return manifest.Funds.Where(f => filter.Contains(f.SeriesId)).ToList();
    }

    private static bool TryParsePeriod(string period, out DateOnly asOf)
    {
        if (DateOnly.TryParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
            return true;
        return DateOnly.TryParse(period, CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf);
    }

    private static string NormalizeCik(string cik)
    {
        var trimmed = cik.Trim();
        return trimmed.TrimStart('0').PadLeft(10, '0');
    }
}
