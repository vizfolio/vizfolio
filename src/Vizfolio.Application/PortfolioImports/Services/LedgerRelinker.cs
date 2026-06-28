using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Instruments;
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
            .Where(t => t.InstrumentId == null && t.Ticker != null)
            .ToListAsync(cancellationToken);

        if (unlinked.Count == 0) return 0;

        var tickers = unlinked
            .Select(t => t.Ticker!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var resolver = new InstrumentResolver(_db, _logger);
        await resolver.PrimeAsync(tickers, cancellationToken);

        var linked = 0;
        foreach (var transaction in unlinked)
        {
            var instrument = resolver.ResolveByTicker(transaction.Ticker!, transaction.Cusip, transaction.CurrencyCode);
            if (instrument is null) continue;
            transaction.LinkToInstrument(instrument.InstrumentId);
            linked++;
        }

        if (linked > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Re-linked {Count} account transactions to instruments", linked);
        }

        return linked;
    }
}
