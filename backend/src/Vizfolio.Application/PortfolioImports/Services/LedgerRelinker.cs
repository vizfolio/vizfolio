using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>
/// Brings the ledger in line with the current reference data: links every transaction that names a security
/// (ticker or CUSIP) but has no holding — creating an unclassified holding when reference data doesn't know it —
/// and promotes unclassified holdings whose ticker reference data now recognises. Safe to run any time; it runs
/// after every import and reference-data refresh.
/// </summary>
public sealed class LedgerRelinker : ILedgerRelinker
{
    private readonly IAppDbContext _db;
    private readonly ILogger<LedgerRelinker> _logger;

    public LedgerRelinker(IAppDbContext db, ILogger<LedgerRelinker> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <returns>The number of transactions newly linked to a holding.</returns>
    public async Task<int> RelinkAsync(CancellationToken cancellationToken = default)
    {
        var unlinked = await _db.AccountTransactions
            .Where(t => t.AccountHoldingId == null && (t.Ticker != null || t.Cusip != null))
            .ToListAsync(cancellationToken);

        var unclassified = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => h.Kind == AccountHoldingKind.Other && h.Symbol != null)
            .Select(h => new { h.AccountId, Symbol = h.Symbol! })
            .ToListAsync(cancellationToken);

        var accountIds = unlinked.Select(t => t.AccountId)
            .Concat(unclassified.Select(h => h.AccountId))
            .Distinct()
            .ToList();
        if (accountIds.Count == 0) return 0;

        var linked = 0;
        foreach (var accountId in accountIds)
        {
            var rows = unlinked.Where(t => t.AccountId == accountId).ToList();
            var symbols = unclassified.Where(h => h.AccountId == accountId).Select(h => h.Symbol).ToList();
            var tickers = rows
                .Where(t => t.Ticker != null)
                .Select(t => t.Ticker!)
                .Concat(symbols)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var resolver = new AccountHoldingResolver(_db, _logger);
            await resolver.PrimeAsync(accountId, tickers, cancellationToken);

            // Promote first, so rows for a newly recognised ticker land on the (promoted) existing holding.
            foreach (var symbol in symbols)
                resolver.ResolveByTicker(symbol, cusip: null, currencyCode: null);

            foreach (var transaction in rows)
            {
                var holding = resolver.ResolveOrCreate(transaction.Ticker, transaction.Cusip, transaction.CurrencyCode);
                if (holding is null) continue;
                transaction.LinkToHolding(holding.AccountHoldingId);
                linked++;
            }
        }

        // Promotions change holdings without linking anything, so save whenever anything changed.
        if (linked > 0 || _db is not DbContext context || context.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Re-linked {Linked} account transactions to holdings", linked);
        }

        return linked;
    }
}
