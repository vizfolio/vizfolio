using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

public interface IImportHistoryService
{
    /// <summary>The portfolio's imports, newest first. Null when the portfolio doesn't exist.</summary>
    Task<ImportHistory?> ListAsync(Guid portfolioId, CancellationToken cancellationToken);
}

/// <summary>A portfolio's imports, and how many of its rows were imported before imports were recorded.</summary>
public sealed record ImportHistory(IReadOnlyList<ImportBatchItem> Imports, int TransactionsImportedBeforeHistory);

/// <summary>One recorded upload, as the Imports list shows it.</summary>
public sealed record ImportBatchItem(
    Guid ImportBatchId,
    string FileName,
    string SourceSystem,
    DateTimeOffset ImportedAt,
    DateTimeOffset? ReprocessedAt,
    // "Active" or "Undone".
    string Status,
    DateTimeOffset? UndoneAt,
    bool HasStoredFile,
    IReadOnlyList<ImportBatchAccount> Accounts,
    IReadOnlyList<ImportWarning> Warnings);

/// <summary>What an import did to one account (from its stored summary), with the account's current name.</summary>
public sealed record ImportBatchAccount(
    Guid AccountId,
    string? AccountName,
    bool Created,
    int Inserted,
    int Skipped,
    int Updated,
    int SnapshotsInserted,
    int Failed);

public sealed class ImportHistoryService : IImportHistoryService
{
    private readonly IAppDbContext _db;

    public ImportHistoryService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<ImportHistory?> ListAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        if (!await _db.Portfolios.AsNoTracking().AnyAsync(p => p.PortfolioId == portfolioId, cancellationToken))
            return null;

        // The stored files aren't needed to list imports, so they aren't loaded.
        var batches = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.PortfolioId == portfolioId)
            .Select(b => new
            {
                b.ImportBatchId, b.FileName, b.ParserSourceSystem, b.ImportedAt, b.ReprocessedAt, b.Status, b.UndoneAt,
                HasStoredFile = b.Content != null, b.SummaryJson,
            })
            .ToListAsync(cancellationToken);

        var accountNames = await _db.Accounts.AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId)
            .ToDictionaryAsync(a => a.AccountId, a => a.Name, cancellationToken);

        var items = batches
            .OrderByDescending(b => b.ImportedAt)
            .Select(b =>
            {
                var summary = PortfolioImportService.ReadSummary(b.SummaryJson);
                var accounts = (summary?.Accounts ?? [])
                    .Select(a => new ImportBatchAccount(
                        a.AccountId, accountNames.GetValueOrDefault(a.AccountId), a.Created, a.Inserted, a.Skipped, a.Updated,
                        a.SnapshotsInserted, a.Failed))
                    .ToList();
                return new ImportBatchItem(
                    b.ImportBatchId, b.FileName, b.ParserSourceSystem, b.ImportedAt, b.ReprocessedAt, b.Status.ToString(), b.UndoneAt,
                    b.HasStoredFile, accounts, summary?.Warnings ?? []);
            })
            .ToList();

        var accountIds = accountNames.Keys.ToList();
        var beforeHistory = await _db.AccountTransactions.AsNoTracking()
            .CountAsync(t => accountIds.Contains(t.AccountId)
                             && t.ImportBatchId == null
                             && t.SourceSystem != ImpliedContributionService.SourceSystem, cancellationToken);

        return new ImportHistory(items, beforeHistory);
    }
}
