using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Application.Extracts.Importers;

public sealed class SecuritiesImporter : ISecuritiesImporter
{
    private readonly IAppDbContext _db;
    private readonly ISecuritiesExtractSource _source;
    private readonly ILogger<SecuritiesImporter> _logger;

    public SecuritiesImporter(
        IAppDbContext db,
        ISecuritiesExtractSource source,
        ILogger<SecuritiesImporter> logger)
    {
        _db = db;
        _source = source;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(SecuritiesImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var manifest = await _source.GetManifestAsync(cancellationToken);

        var targetCiks = ResolveTargetCiks(manifest, options.Tickers);
        if (targetCiks.Count == 0)
        {
            stopwatch.Stop();
            return ImportResult.Empty(stopwatch.Elapsed);
        }

        var gate = await ReferenceCodeGate.LoadAsync(_db, cancellationToken);

        var existing = await _db.Securities
            .Where(s => targetCiks.Contains(s.Cik))
            .ToDictionaryAsync(s => s.Cik, cancellationToken);

        var upserted = 0;
        var skipped = 0;
        var failures = new List<ImportFailure>();

        foreach (var cik in targetCiks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var extract = await _source.GetSecurityAsync(cik, cancellationToken);
                if (extract is null)
                {
                    failures.Add(new ImportFailure(cik, "Security extract not found."));
                    continue;
                }

                var normalizedCik = NormalizeCik(extract.Cik);
                existing.TryGetValue(normalizedCik, out var security);

                if (security is not null && !options.Force && extract.Source.EdgarFetchedAt <= security.EdgarFetchedAt)
                {
                    skipped++;
                    continue;
                }

                if (security is null)
                {
                    security = new Security(normalizedCik, extract.Source.EdgarFetchedAt);
                    _db.Securities.Add(security);
                    existing[normalizedCik] = security;
                }
                else if (extract.Source.EdgarFetchedAt >= security.EdgarFetchedAt)
                {
                    security.MarkRefreshed(extract.Source.EdgarFetchedAt);
                }

                security.UpdateProfile(
                    extract.Name,
                    extract.EntityType,
                    extract.Sector,
                    extract.SicDescription,
                    gate.AcceptCountry(extract.Country));
                security.SetTickers(extract.Tickers ?? Array.Empty<string>());
                security.SetExchanges(extract.Exchanges ?? Array.Empty<string>());

                upserted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import security {Cik}", cik);
                failures.Add(new ImportFailure(cik, ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        gate.LogReport(_logger);

        stopwatch.Stop();
        return new ImportResult(
            Considered: targetCiks.Count,
            Upserted: upserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures,
            DataCleaning: gate.BuildReport(),
            Duration: stopwatch.Elapsed);
    }

    private static List<string> ResolveTargetCiks(SecuritiesManifest manifest, IReadOnlyList<string>? tickerFilter)
    {
        if (tickerFilter is null || tickerFilter.Count == 0)
        {
            return manifest.ByTicker.Values
                .Select(NormalizeCik)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var filter = new HashSet<string>(tickerFilter
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant()));

        return manifest.ByTicker
            .Where(kvp => filter.Contains(kvp.Key.ToUpperInvariant()))
            .Select(kvp => NormalizeCik(kvp.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeCik(string cik)
    {
        var trimmed = cik.Trim();
        return trimmed.TrimStart('0').PadLeft(10, '0');
    }
}
