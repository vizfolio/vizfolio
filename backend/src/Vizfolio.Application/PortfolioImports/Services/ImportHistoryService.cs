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

    /// <summary>
    /// The file an import stored, as uploaded (undone imports included). Null when the import isn't in the portfolio or
    /// its file wasn't kept.
    /// </summary>
    Task<StoredImportFile?> GetFileAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken);
}

/// <summary>An uploaded file, decompressed, with its original name and content type.</summary>
public sealed record StoredImportFile(string FileName, string ContentType, byte[] Content);

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

    public async Task<StoredImportFile?> GetFileAsync(Guid portfolioId, Guid importBatchId, CancellationToken cancellationToken)
    {
        var batch = await _db.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ImportBatchId == importBatchId && b.PortfolioId == portfolioId, cancellationToken);
        if (batch?.Content is null) return null;

        var file = ImportFile.FromBatch(batch);
        return new StoredImportFile(batch.FileName, batch.ContentType ?? "application/octet-stream", file.Content);
    }
}
