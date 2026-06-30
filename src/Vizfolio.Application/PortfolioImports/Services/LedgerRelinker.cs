using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

public sealed class LedgerRelinker : ILedgerRelinker
{
    private readonly IAppDbContext _db;
    private readonly ILogger<LedgerRelinker> _logger;

    public LedgerRelinker(IAppDbContext db, ILogger<LedgerRelinker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> RelinkAsync(CancellationToken cancellationToken = default)
    {
        var unlinked = await _db.AccountTransactions
            .Where(t => t.AccountHoldingId == null && t.Ticker != null)
            .ToListAsync(cancellationToken);

        if (unlinked.Count == 0) return 0;

        var linked = 0;
        foreach (var byAccount in unlinked.GroupBy(t => t.AccountId))
        {
            var tickers = byAccount
                .Select(t => t.Ticker!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var resolver = new AccountHoldingResolver(_db, _logger);
            await resolver.PrimeAsync(byAccount.Key, tickers, cancellationToken);

            foreach (var transaction in byAccount)
            {
                var holding = resolver.ResolveByTicker(transaction.Ticker!, transaction.Cusip, transaction.CurrencyCode);
                if (holding is null) continue;
                transaction.LinkToHolding(holding.AccountHoldingId);
                linked++;
            }
        }

        if (linked > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Re-linked {Count} account transactions to holdings", linked);
        }

        return linked;
    }
}
