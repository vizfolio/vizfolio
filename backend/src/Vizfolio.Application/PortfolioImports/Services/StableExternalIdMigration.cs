using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

public interface IStableExternalIdMigration
{
    /// <summary>Rewrites synthetic ids to their stable form; returns how many rows changed (0 once done).</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Rows from formats without row ids (e.g. the Vanguard report) used to get <c>"{fingerprint}-{row number in the
/// file}"</c>, so the same transaction had a different id in every export. Imports now use
/// <c>"{fingerprint}-{occurrence}"</c> — the n-th identical row — which is the same in every export, and which
/// future user corrections key on. This rewrites stored ids deterministically: per account, source and fingerprint,
/// rows ordered by import time then old suffix get occurrences 0, 1, 2… Idempotent (a no-op once done), so it simply
/// runs at every startup.
/// </summary>
public sealed partial class StableExternalIdMigration : IStableExternalIdMigration
{
    private readonly IAppDbContext _db;
    private readonly ILogger<StableExternalIdMigration> _logger;

    public StableExternalIdMigration(IAppDbContext db, ILogger<StableExternalIdMigration> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Synthetic ids are 32 hex characters, a dash and a number; provider ids (FITIDs) are left alone.
        var candidates = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.SourceSystem != ImpliedContributionService.SourceSystem && t.ExternalId.Length > 33)
            .Select(t => new
            {
                t.AccountTransactionId, t.AccountId, t.SourceSystem, t.ExternalId, t.TradeDate, t.Ticker, t.Quantity,
                t.Amount, t.ImportedAt,
            })
            .ToListAsync(cancellationToken);

        var renames = candidates
            .Select(r => (Row: r, Match: SyntheticId().Match(r.ExternalId)))
            .Where(x => x.Match.Success)
            .GroupBy(x => (x.Row.AccountId, x.Row.SourceSystem,
                Fingerprint: TransactionFingerprint.Compute(x.Row.AccountId, x.Row.TradeDate, x.Row.Ticker, x.Row.Quantity, x.Row.Amount)))
            .SelectMany(g => g
                .OrderBy(x => x.Row.ImportedAt)
                .ThenBy(x => long.Parse(x.Match.Groups["n"].Value, CultureInfo.InvariantCulture))
                .Select((x, occurrence) => (x.Row.AccountTransactionId, x.Row.ExternalId, Stable: $"{g.Key.Fingerprint}-{occurrence}")))
            .Where(x => !string.Equals(x.ExternalId, x.Stable, StringComparison.Ordinal))
            .ToDictionary(x => x.AccountTransactionId, x => x.Stable);
        if (renames.Count == 0) return 0;

        var ids = renames.Keys.ToList();
        await _db.ExecuteInTransactionAsync(async () =>
        {
            var rows = await _db.AccountTransactions
                .Where(t => ids.Contains(t.AccountTransactionId))
                .ToListAsync(cancellationToken);

            // Two passes: ids can swap within a group, and (account, source, id) is unique at every step.
            foreach (var row in rows) row.ReassignExternalId($"~{row.AccountTransactionId:N}");
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var row in rows) row.ReassignExternalId(renames[row.AccountTransactionId]);
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _logger.LogInformation("Rewrote {Count} synthetic transaction ids to their stable form", renames.Count);
        return renames.Count;
    }

    [GeneratedRegex("^[0-9a-f]{32}-(?<n>[0-9]+)$")]
    private static partial Regex SyntheticId();
}
