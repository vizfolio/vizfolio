using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Abstractions;

namespace Vizfolio.Application.Extracts.Importers;

public sealed class HoldingRelinker : IHoldingRelinker
{
    private readonly IAppDbContext _db;
    private readonly ILogger<HoldingRelinker> _logger;

    public HoldingRelinker(IAppDbContext db, ILogger<HoldingRelinker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> RelinkAsync(CancellationToken cancellationToken = default)
    {
        var unlinked = await _db.FundHoldings
            .Where(h => h.SecurityId == null && h.IssuerCik != null)
            .ToListAsync(cancellationToken);

        if (unlinked.Count == 0)
            return 0;

        var ciks = unlinked.Select(h => h.IssuerCik!).Distinct(StringComparer.Ordinal).ToList();
        var securityIdByCik = await _db.Securities
            .Where(s => ciks.Contains(s.Cik))
            .ToDictionaryAsync(s => s.Cik, s => s.SecurityId, cancellationToken);

        var linked = 0;
        foreach (var holding in unlinked)
        {
            if (holding.IssuerCik is null) continue;
            if (!securityIdByCik.TryGetValue(holding.IssuerCik, out var securityId)) continue;

            holding.LinkToSecurity(securityId);
            linked++;
        }

        if (linked > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Re-linked {Count} fund holdings to securities by issuer CIK", linked);
        }

        return linked;
    }
}
