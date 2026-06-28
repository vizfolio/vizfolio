using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;

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
            .Where(t => t.SecurityId == null && t.Ticker != null)
            .ToListAsync(cancellationToken);

        if (unlinked.Count == 0) return 0;

        var tickers = unlinked
            .Select(t => t.Ticker!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var securities = await _db.Securities.AsNoTracking().ToListAsync(cancellationToken);

        var securityIdByTicker = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var security in securities)
        {
            foreach (var ticker in security.Tickers)
            {
                if (tickers.Contains(ticker)) securityIdByTicker.TryAdd(ticker, security.SecurityId);
            }
        }

        var linked = 0;
        foreach (var transaction in unlinked)
        {
            if (transaction.Ticker is null) continue;
            if (!securityIdByTicker.TryGetValue(transaction.Ticker, out var securityId)) continue;
            transaction.LinkToSecurity(securityId);
            linked++;
        }

        if (linked > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Re-linked {Count} account transactions to securities by ticker", linked);
        }

        return linked;
    }
}
