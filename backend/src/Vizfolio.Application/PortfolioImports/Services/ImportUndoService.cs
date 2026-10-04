using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

public interface IImportUndoService
{
    /// <summary>What undoing the import would remove or revert. Nothing is written.</summary>
    Task<ImportUndoOutcome> PreviewAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken);

    /// <summary>Reverses the import (see <see cref="ImportUndoService"/>).</summary>
    Task<ImportUndoOutcome> UndoAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken);
}

public enum ImportUndoStatus
{
    Ok,
    NotFound,
    AlreadyUndone,
}

public sealed record ImportUndoOutcome(ImportUndoStatus Status, ImportUndoSummary? Summary)
{
    public static readonly ImportUndoOutcome NotFound = new(ImportUndoStatus.NotFound, null);
    public static readonly ImportUndoOutcome AlreadyUndone = new(ImportUndoStatus.AlreadyUndone, null);
}

/// <summary>
/// What an undo removes or reverts (a preview), or did. <see cref="LaterImportsReplayed"/> are later imports into the
/// same accounts, re-run from their stored files so rows they skipped as duplicates of this import's come back;
/// <see cref="LaterImportsWithoutFile"/> can't be (re-upload them to be sure nothing is missing).
/// </summary>
public sealed record ImportUndoSummary(
    Guid ImportBatchId,
    string FileName,
    int Transactions,
    int Snapshots,
    int UpdatesReverted,
    int HoldingsRemoved,
    int AccountsRemoved,
    IReadOnlyList<string> LaterImportsReplayed,
    IReadOnlyList<string> LaterImportsWithoutFile);

/// <summary>
/// Reverses one import (roadmap Appendix C.6), all in one database transaction:
/// <list type="number">
///   <item>reverts the stored rows the import updated — only where a row still holds the values it set;</item>
///   <item>deletes the transactions and snapshots it inserted (rows it skipped as duplicates were never tagged with
///   it, so other imports' data is never touched, nor are rows imported before imports were recorded);</item>
///   <item>removes holdings and accounts it created that nothing else now uses;</item>
///   <item>marks it undone (kept for the record; the same file can then be imported again);</item>
///   <item>replays later imports into the same accounts from their stored files, so any row they skipped as a
///   duplicate of this import's comes back from them;</item>
///   <item>relinks holdings and re-derives implied contributions for the affected accounts.</item>
/// </list>
/// </summary>
public sealed class ImportUndoService : IImportUndoService
{
    private readonly IAppDbContext _db;
    private readonly IPortfolioImportService _imports;
    private readonly IImpliedContributionService _impliedContributions;
    private readonly ILedgerRelinker _relinker;

    public ImportUndoService(
        IAppDbContext db,
        IPortfolioImportService imports,
        IImpliedContributionService impliedContributions,
        ILedgerRelinker relinker)
    {
        _db = db;
        _imports = imports;
        _impliedContributions = impliedContributions;
        _relinker = relinker;
    }

    public async Task<ImportUndoOutcome> PreviewAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken)
    {
        var batch = await FindAsync(portfolioId, importBatchId, cancellationToken);
        if (batch is null) return ImportUndoOutcome.NotFound;
        if (batch.IsUndone) return ImportUndoOutcome.AlreadyUndone;

        var plan = await PlanAsync(batch, cancellationToken);
        return new ImportUndoOutcome(ImportUndoStatus.Ok, plan.Summary);
    }

    public async Task<ImportUndoOutcome> UndoAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken)
    {
        var batch = await FindAsync(portfolioId, importBatchId, cancellationToken);
        if (batch is null) return ImportUndoOutcome.NotFound;
        if (batch.IsUndone) return ImportUndoOutcome.AlreadyUndone;

        ImportUndoSummary? summary = null;
        await _db.ExecuteInTransactionAsync(async () =>
        {
            var plan = await PlanAsync(batch, cancellationToken);
            summary = plan.Summary;

            await RevertUpdatesAsync(batch.ImportBatchId, cancellationToken);
            await DeleteInsertedAsync(batch.ImportBatchId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var (holdings, accounts) = await RemoveUnusedCreationsAsync(batch.ImportBatchId, cancellationToken);
            batch.MarkUndone();
            await _db.SaveChangesAsync(cancellationToken);

            // Later imports re-run from their stored files restore rows they skipped as this import's duplicates.
            if (plan.LaterBatchIds.Count > 0)
                await _imports.ReprocessAsync(batch.PortfolioId, plan.LaterBatchIds, cancellationToken);

            await _relinker.RelinkAsync(cancellationToken);
            var remaining = await _db.Accounts.AsNoTracking()
                .Where(a => plan.AccountIds.Contains(a.AccountId))
                .Select(a => a.AccountId)
                .ToListAsync(cancellationToken);
            foreach (var accountId in remaining)
                await _impliedContributions.SyncForAccountAsync(accountId, cancellationToken);

            summary = summary with { HoldingsRemoved = holdings, AccountsRemoved = accounts };
        }, cancellationToken);

        return new ImportUndoOutcome(ImportUndoStatus.Ok, summary);
    }

    private Task<ImportBatch?> FindAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken)
        => _db.ImportBatches.FirstOrDefaultAsync(
            b => b.ImportBatchId == importBatchId && b.PortfolioId == portfolioId, cancellationToken);

    private sealed record UndoPlan(ImportUndoSummary Summary, List<Guid> AccountIds, List<Guid> LaterBatchIds);

    /// <summary>Works out what the undo touches, without writing.</summary>
    private async Task<UndoPlan> PlanAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        var batchId = batch.ImportBatchId;

        var rows = await _db.AccountTransactions.AsNoTracking()
            .Where(t => t.ImportBatchId == batchId)
            .Select(t => new { t.AccountId, t.AccountHoldingId })
            .ToListAsync(cancellationToken);
        var snapshots = await _db.AccountHoldingSnapshots.AsNoTracking()
            .Where(s => s.ImportBatchId == batchId)
            .Join(_db.AccountHoldings, s => s.AccountHoldingId, h => h.AccountHoldingId, (s, h) => new { h.AccountId })
            .ToListAsync(cancellationToken);

        var updates = await _db.ImportBatchRowUpdates.AsNoTracking()
            .Where(u => u.ImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var updatedIds = updates.Select(u => u.AccountTransactionId).Distinct().ToList();
        var updatedRows = await _db.AccountTransactions.AsNoTracking()
            .Where(t => updatedIds.Contains(t.AccountTransactionId) && t.ImportBatchId != batchId)
            .ToListAsync(cancellationToken);
        var revertible = updates
            .Count(u => updatedRows.FirstOrDefault(r => r.AccountTransactionId == u.AccountTransactionId) is { } row
                        && ImportedTransactionFields.Of(row) == u.Next);

        var accountIds = rows.Select(r => r.AccountId)
            .Concat(snapshots.Select(s => s.AccountId))
            .Concat(updatedRows.Select(r => r.AccountId))
            .Concat(batch.AccountId is { } a ? [a] : [])
            .Concat(PortfolioImportService.ReadSummary(batch)?.Accounts.Select(x => x.AccountId) ?? [])
            .Distinct()
            .ToList();

        var (holdingsRemoved, accountsRemoved) = await CountUnusedCreationsAsync(batchId, cancellationToken);

        // Later imports into the same accounts, oldest first.
        var later = (await _db.ImportBatches.AsNoTracking()
                .Where(b => b.PortfolioId == batch.PortfolioId && b.ImportBatchId != batchId && b.Status == ImportBatchStatus.Active)
                .Select(b => new { b.ImportBatchId, b.FileName, b.ImportedAt, b.AccountId, b.SummaryJson, HasFile = b.Content != null })
                .ToListAsync(cancellationToken))
            .Where(b => b.ImportedAt > batch.ImportedAt)
            .Where(b => (b.AccountId is { } id && accountIds.Contains(id))
                        || (PortfolioImportService.ReadSummary(b.SummaryJson)?.Accounts.Any(x => accountIds.Contains(x.AccountId)) ?? false))
            .OrderBy(b => b.ImportedAt)
            .ToList();

        var summary = new ImportUndoSummary(
            batchId,
            batch.FileName,
            rows.Count,
            snapshots.Count,
            revertible,
            holdingsRemoved,
            accountsRemoved,
            later.Where(b => b.HasFile).Select(b => b.FileName).ToList(),
            later.Where(b => !b.HasFile).Select(b => b.FileName).ToList());
        return new UndoPlan(summary, accountIds, later.Where(b => b.HasFile).Select(b => b.ImportBatchId).ToList());
    }

    /// <summary>
    /// Puts back the values the import overwrote, newest change first — only where the row still holds what the
    /// import set (a later import may have changed it again, and that change stands).
    /// </summary>
    private async Task RevertUpdatesAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var updates = (await _db.ImportBatchRowUpdates.AsNoTracking()
                .Where(u => u.ImportBatchId == batchId)
                .ToListAsync(cancellationToken))
            .OrderByDescending(u => u.RecordedAt)
            .ToList();
        if (updates.Count == 0) return;

        var ids = updates.Select(u => u.AccountTransactionId).Distinct().ToList();
        var rows = await _db.AccountTransactions
            .Where(t => ids.Contains(t.AccountTransactionId) && t.ImportBatchId != batchId)
            .ToDictionaryAsync(t => t.AccountTransactionId, cancellationToken);
        foreach (var update in updates)
        {
            if (rows.TryGetValue(update.AccountTransactionId, out var row) && ImportedTransactionFields.Of(row) == update.Next)
                row.ApplyImportedFields(update.Previous);
        }
    }

    /// <summary>Deletes the import's rows and snapshots, and any record of later imports updating those rows.</summary>
    private async Task DeleteInsertedAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var rows = await _db.AccountTransactions.Where(t => t.ImportBatchId == batchId).ToListAsync(cancellationToken);
        var rowIds = rows.Select(r => r.AccountTransactionId).ToList();
        var orphanedUpdates = await _db.ImportBatchRowUpdates
            .Where(u => rowIds.Contains(u.AccountTransactionId))
            .ToListAsync(cancellationToken);

        _db.ImportBatchRowUpdates.RemoveRange(orphanedUpdates);
        _db.AccountTransactions.RemoveRange(rows);
        _db.AccountHoldingSnapshots.RemoveRange(
            await _db.AccountHoldingSnapshots.Where(s => s.ImportBatchId == batchId).ToListAsync(cancellationToken));
    }

    private async Task<(int Holdings, int Accounts)> CountUnusedCreationsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var holdings = await UnusedHoldingsQuery(batchId, excludeBatchRows: true).CountAsync(cancellationToken);
        var accounts = await UnusedAccountsQuery(batchId, excludeBatchRows: true).CountAsync(cancellationToken);
        return (holdings, accounts);
    }

    /// <summary>
    /// Removes holdings, then accounts, the import created that nothing else uses now its rows are gone. An account
    /// is removed only once it has no holdings and no imported rows (its implied contributions go with it).
    /// </summary>
    private async Task<(int Holdings, int Accounts)> RemoveUnusedCreationsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var holdings = await UnusedHoldingsQuery(batchId, excludeBatchRows: false).ToListAsync(cancellationToken);
        _db.AccountHoldings.RemoveRange(holdings);
        await _db.SaveChangesAsync(cancellationToken);

        var accounts = await UnusedAccountsQuery(batchId, excludeBatchRows: false).ToListAsync(cancellationToken);
        _db.Accounts.RemoveRange(accounts);
        return (holdings.Count, accounts.Count);
    }

    // With excludeBatchRows, the batch's own rows/snapshots count as already gone (for the preview); otherwise they
    // have been deleted and the plain query is exact.
    private IQueryable<AccountHolding> UnusedHoldingsQuery(Guid batchId, bool excludeBatchRows)
        => _db.AccountHoldings.Where(h =>
            h.CreatedByImportBatchId == batchId
            && !_db.AccountTransactions.Any(t => t.AccountHoldingId == h.AccountHoldingId
                                                 && (!excludeBatchRows || t.ImportBatchId == null || t.ImportBatchId != batchId))
            && !_db.AccountHoldingSnapshots.Any(s => s.AccountHoldingId == h.AccountHoldingId
                                                     && (!excludeBatchRows || s.ImportBatchId == null || s.ImportBatchId != batchId)));

    private IQueryable<Account> UnusedAccountsQuery(Guid batchId, bool excludeBatchRows)
        => _db.Accounts.Where(a =>
            a.CreatedByImportBatchId == batchId
            && !_db.AccountTransactions.Any(t => t.AccountId == a.AccountId
                                                 && t.SourceSystem != ImpliedContributionService.SourceSystem
                                                 && (!excludeBatchRows || t.ImportBatchId == null || t.ImportBatchId != batchId))
            && !_db.AccountHoldings.Any(h => h.AccountId == a.AccountId
                                             && (!excludeBatchRows || h.CreatedByImportBatchId == null || h.CreatedByImportBatchId != batchId
                                                 || _db.AccountTransactions.Any(t => t.AccountHoldingId == h.AccountHoldingId
                                                     && (t.ImportBatchId == null || t.ImportBatchId != batchId))
                                                 || _db.AccountHoldingSnapshots.Any(s => s.AccountHoldingId == h.AccountHoldingId
                                                     && (s.ImportBatchId == null || s.ImportBatchId != batchId)))));
}
