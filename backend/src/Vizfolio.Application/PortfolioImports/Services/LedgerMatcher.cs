using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>
/// Decides, row by row, whether an incoming transaction is already in an account's ledger:
/// <list type="bullet">
///   <item>by <c>(source, externalId)</c> — the same row from the same source (a re-upload, or a later export of the
///   same history);</item>
///   <item>else by economic fingerprint against rows from <i>any</i> source (the same trade in a QFX and a Vanguard
///   report). Each stored row covers one incoming row at most, so genuine same-day duplicates are still added.</item>
/// </list>
/// A match against a row from the same source is returned so the importer can bring it in line with the parser.
/// Vizfolio's derived rows (implied contributions) are never matched: a real deposit arriving later must not be
/// skipped as a duplicate of one.
/// </summary>
internal sealed class LedgerMatcher
{
    private readonly Dictionary<string, StoredRow> _sameSourceById;
    private readonly Dictionary<string, Queue<StoredRow>> _sameSourceByFingerprint;
    private readonly Dictionary<string, int> _unclaimed;
    private readonly HashSet<Guid> _claimed = [];
    private readonly HashSet<string> _insertedIds = new(StringComparer.Ordinal);

    private LedgerMatcher(Guid accountId, string sourceSystem, IReadOnlyList<StoredRow> rows)
    {
        // Cross-source fingerprint multiset: how many stored rows of each fingerprint are still unclaimed.
        _unclaimed = rows
            .GroupBy(r => r.Fingerprint(accountId))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var sameSource = rows.Where(r => string.Equals(r.SourceSystem, sourceSystem, StringComparison.OrdinalIgnoreCase)).ToList();
        _sameSourceById = sameSource
            .GroupBy(r => r.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _sameSourceByFingerprint = sameSource
            .GroupBy(r => r.Fingerprint(accountId))
            .ToDictionary(g => g.Key, g => new Queue<StoredRow>(g), StringComparer.Ordinal);
        AccountId = accountId;
    }

    private Guid AccountId { get; }

    /// <summary>A stored row as the matcher sees it.</summary>
    public sealed record StoredRow(
        Guid Id, string SourceSystem, string ExternalId, DateOnly TradeDate, string? Ticker, ImportedTransactionFields Fields)
    {
        public string Fingerprint(Guid accountId)
            => TransactionFingerprint.Compute(accountId, TradeDate, Ticker, Fields.Quantity, Fields.Amount);
    }

    /// <summary><see cref="IsDuplicate"/>: skip the incoming row. <see cref="SameSourceRow"/>: the stored row it repeats, if from this source.</summary>
    public readonly record struct MatchResult(bool IsDuplicate, StoredRow? SameSourceRow);

    public static async Task<LedgerMatcher> LoadAsync(
        IAppDbContext db, Guid accountId, string sourceSystem, bool accountIsNew, CancellationToken cancellationToken)
    {
        var rows = accountIsNew
            ? []
            : await db.AccountTransactions
                .AsNoTracking()
                .Where(t => t.AccountId == accountId && t.SourceSystem != ImpliedContributionService.SourceSystem)
                .Select(t => new StoredRow(
                    t.AccountTransactionId, t.SourceSystem, t.ExternalId, t.TradeDate, t.Ticker,
                    new ImportedTransactionFields(t.Type, t.Amount, t.Quantity, t.Price, t.SettlementDate, t.SourceType, t.IsSettlementFund)))
                .ToListAsync(cancellationToken);
        return new LedgerMatcher(accountId, sourceSystem.Trim().ToUpperInvariant(), rows);
    }

    public MatchResult Match(string externalId, string fingerprint)
    {
        // The same id twice in one file: the first was just inserted.
        if (_insertedIds.Contains(externalId)) return new MatchResult(true, null);

        if (_sameSourceById.TryGetValue(externalId, out var byId))
        {
            // The stored row is now covered, so it can't also absorb an identical row later in the file (a later
            // export lists N identical rows as occurrences 0..N-1, each matching its own id).
            if (_claimed.Add(byId.Id))
                Release(byId.Fingerprint(AccountId));
            else if (Release(byId.Fingerprint(AccountId)))
                ClaimNextSameSource(byId.Fingerprint(AccountId)); // a fingerprint match took this row; it covered another
            return new MatchResult(true, byId);
        }

        if (Release(fingerprint))
            return new MatchResult(true, ClaimNextSameSource(fingerprint));

        return new MatchResult(false, null);
    }

    public void Inserted(string externalId) => _insertedIds.Add(externalId);

    private bool Release(string fingerprint)
    {
        if (!_unclaimed.TryGetValue(fingerprint, out var remaining) || remaining <= 0) return false;
        _unclaimed[fingerprint] = remaining - 1;
        return true;
    }

    private StoredRow? ClaimNextSameSource(string fingerprint)
    {
        if (!_sameSourceByFingerprint.TryGetValue(fingerprint, out var queue)) return null;
        while (queue.TryDequeue(out var row))
            if (_claimed.Add(row.Id)) return row;
        return null;
    }
}
