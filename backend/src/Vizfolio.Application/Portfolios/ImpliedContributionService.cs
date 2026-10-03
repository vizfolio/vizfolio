using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

public interface IImpliedContributionService
{
    /// <summary>
    /// Dry run: the contributions the account's ledger implies but never recorded. Nothing is written.
    /// Returns null when the account isn't in the portfolio.
    /// </summary>
    Task<ImpliedContributionPreview?> PreviewForAccountAsync(
        Guid portfolioId, Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Recomputes the account's implied contributions from its real (imported) rows and stores them as
    /// labelled <see cref="TransactionType.Deposit"/> rows, replacing any previous ones — so a later import
    /// that brings in the real deposit makes the implied one disappear. Saves its own changes.
    /// </summary>
    Task<ImpliedContributionSyncResult> SyncForAccountAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>The account's implied contributions after a sync.</summary>
public sealed record ImpliedContributionSyncResult(int Count, decimal TotalAmount);

public sealed record ImpliedContributionPreview(
    Guid AccountId,
    decimal Tolerance,
    decimal TotalAmount,
    decimal EndingCash,
    IReadOnlyList<ImpliedContributionYear> ByYear,
    IReadOnlyList<ImpliedContribution> Contributions);

public sealed record ImpliedContributionYear(int Year, decimal Amount, int Count);

public sealed class ImpliedContributionService : IImpliedContributionService
{
    /// <summary>Source system of rows Vizfolio derives itself; never produced by an import parser.</summary>
    public const string SourceSystem = "VIZFOLIO";

    /// <summary>Label shown on implied rows (their <see cref="AccountTransaction.SourceType"/>).</summary>
    public const string SourceType = "Implied contribution";

    private readonly IAppDbContext _db;
    private readonly AccountValuationLoader _valuationLoader;

    public ImpliedContributionService(IAppDbContext db, AccountValuationLoader valuationLoader)
    {
        _db = db;
        _valuationLoader = valuationLoader;
    }

    public async Task<ImpliedContributionPreview?> PreviewForAccountAsync(
        Guid portfolioId, Guid accountId, CancellationToken cancellationToken)
    {
        var inScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == portfolioId && a.AccountId == accountId, cancellationToken);
        if (!inScope) return null;

        var rows = await LoadImportedRowsAsync(accountId, cancellationToken);
        var openingCash = await OpeningCashAsync(accountId, cancellationToken);

        const decimal tolerance = ImpliedContributionCalculator.DefaultTolerance;
        var contributions = ImpliedContributionCalculator.Find(rows, tolerance, openingCash);

        var byYear = contributions
            .GroupBy(c => c.Date.Year)
            .OrderBy(g => g.Key)
            .Select(g => new ImpliedContributionYear(g.Key, g.Sum(c => c.Amount), g.Count()))
            .ToList();

        return new ImpliedContributionPreview(
            accountId,
            tolerance,
            contributions.Sum(c => c.Amount),
            ImpliedContributionCalculator.EndingCash(rows, tolerance, openingCash),
            byYear,
            contributions);
    }

    public async Task<ImpliedContributionSyncResult> SyncForAccountAsync(
        Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await LoadImportedRowsAsync(accountId, cancellationToken);
        var openingCash = await OpeningCashAsync(accountId, cancellationToken);
        var desired = ImpliedContributionCalculator.Find(rows, openingCash: openingCash)
            .ToDictionary(c => ExternalIdFor(c.Date), c => c);

        var existing = await _db.AccountTransactions
            .Where(t => t.AccountId == accountId && t.SourceSystem == SourceSystem)
            .ToListAsync(cancellationToken);

        // Keep rows that are still implied unchanged; drop the rest. Removals are saved before inserts so a
        // re-dated or re-sized contribution can reuse its (account, source, external id) key.
        var stale = existing
            .Where(t => !desired.TryGetValue(t.ExternalId, out var c) || c.Amount != t.Amount)
            .ToList();
        if (stale.Count > 0)
        {
            _db.AccountTransactions.RemoveRange(stale);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var kept = existing.Except(stale).Select(t => t.ExternalId).ToHashSet(StringComparer.Ordinal);
        foreach (var (externalId, contribution) in desired.Where(d => !kept.Contains(d.Key)))
        {
            var row = new AccountTransaction(
                accountId, SourceSystem, externalId, TransactionType.Deposit, contribution.Date, contribution.Amount);
            row.SetSourceType(SourceType);
            row.SetMemo("Purchase with no recorded deposit — funded from outside the account.");
            _db.AccountTransactions.Add(row);
        }
        await _db.SaveChangesAsync(cancellationToken);

        return new ImpliedContributionSyncResult(desired.Count, desired.Values.Sum(c => c.Amount));
    }

    /// <summary>The account's imported rows — its own implied rows excluded, so they never feed back in.</summary>
    private Task<List<CashLedgerRow>> LoadImportedRowsAsync(Guid accountId, CancellationToken cancellationToken)
        => _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.SourceSystem != SourceSystem)
            .Select(t => new CashLedgerRow(
                t.TradeDate, t.Type, t.Amount, t.Quantity, t.Ticker, t.SourceType, t.SettlementDate))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Cash the account already held before its imported history, derived from the broker's statements over the
    /// imported rows only (Vizfolio's own implied rows never feed back in). Zero for a complete history.
    /// </summary>
    private async Task<decimal> OpeningCashAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var loaded = await _valuationLoader.LoadAsync(
            [accountId], DateOnly.MaxValue, cancellationToken, includeImplied: false, includePrices: false);
        return loaded.Engines.TryGetValue(accountId, out var engine) ? engine.OpeningCashSeed : 0m;
    }

    // One implied contribution per trading day at most, so the date is a stable, unique key.
    private static string ExternalIdFor(DateOnly date) => $"implied-{date:yyyy-MM-dd}";
}
