using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Application.Portfolios;

public sealed class PortfolioPerformanceService : IPortfolioPerformanceService
{
    private const string DefaultCurrencyCode = "USD";

    private readonly IAppDbContext _db;

    public PortfolioPerformanceService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PortfolioPerformanceResult?> ComputeForPortfolioAsync(
        Guid portfolioId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var portfolioExists = await _db.Portfolios
            .AsNoTracking()
            .AnyAsync(p => p.PortfolioId == portfolioId, cancellationToken);
        if (!portfolioExists) return null;

        var accountIds = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId)
            .Select(a => a.AccountId)
            .ToListAsync(cancellationToken);

        return await ComputeAsync(accountIds, from, to, cancellationToken);
    }

    public async Task<PortfolioPerformanceResult?> ComputeForAccountAsync(
        Guid portfolioId,
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var accountInScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == portfolioId && a.AccountId == accountId, cancellationToken);
        if (!accountInScope) return null;

        return await ComputeAsync(new[] { accountId }, from, to, cancellationToken);
    }

    private async Task<PortfolioPerformanceResult?> ComputeAsync(
        IReadOnlyList<Guid> accountIds,
        DateOnly? fromInput,
        DateOnly? toInput,
        CancellationToken cancellationToken)
    {
        var resolvedTo = toInput ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (accountIds.Count == 0)
        {
            var resolvedFromEmpty = fromInput ?? resolvedTo;
            if (resolvedFromEmpty > resolvedTo) return null;
            return Empty(resolvedFromEmpty, resolvedTo);
        }

        var holdingIds = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => accountIds.Contains(h.AccountId))
            .Select(h => h.AccountHoldingId)
            .ToListAsync(cancellationToken);
        if (holdingIds.Count == 0)
        {
            var resolvedFromNoHoldings = fromInput ?? resolvedTo;
            if (resolvedFromNoHoldings > resolvedTo) return null;
            return Empty(resolvedFromNoHoldings, resolvedTo);
        }

        var from = fromInput ?? await ResolveDefaultFromAsync(
            holdingIds, accountIds, resolvedTo, cancellationToken);
        var to = resolvedTo;

        if (from > to) return null;

        var snapshots = await _db.AccountHoldingSnapshots
            .AsNoTracking()
            .Where(s => holdingIds.Contains(s.AccountHoldingId) && s.AsOf <= to)
            .Select(s => new SnapshotProjection(s.AccountHoldingId, s.AsOf, s.MarketValue, s.CurrencyCode))
            .ToListAsync(cancellationToken);

        var holdingsActiveInRange = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountHoldingId != null
                        && holdingIds.Contains(t.AccountHoldingId!.Value)
                        && t.TradeDate >= from
                        && t.TradeDate <= to)
            .Select(t => t.AccountHoldingId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var snapshotsByHolding = snapshots
            .GroupBy(s => s.AccountHoldingId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.AsOf).ToList());

        var relevantHoldings = new HashSet<Guid>(snapshotsByHolding.Keys);
        foreach (var id in holdingsActiveInRange) relevantHoldings.Add(id);

        var currency = ResolveReportingCurrency(snapshots);

        var ending = ComputeBalance(relevantHoldings, snapshotsByHolding, to);
        var starting = ComputeBalance(relevantHoldings, snapshotsByHolding, from);

        return new PortfolioPerformanceResult(from, to, starting, ending, currency);
    }

    private async Task<DateOnly> ResolveDefaultFromAsync(
        IReadOnlyList<Guid> holdingIds,
        IReadOnlyList<Guid> accountIds,
        DateOnly fallback,
        CancellationToken cancellationToken)
    {
        var earliestTradeDate = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId))
            .OrderBy(t => t.TradeDate)
            .Select(t => (DateOnly?)t.TradeDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (earliestTradeDate.HasValue) return earliestTradeDate.Value;

        var earliestSnapshot = await _db.AccountHoldingSnapshots
            .AsNoTracking()
            .Where(s => holdingIds.Contains(s.AccountHoldingId))
            .OrderBy(s => s.AsOf)
            .Select(s => (DateOnly?)s.AsOf)
            .FirstOrDefaultAsync(cancellationToken);
        return earliestSnapshot ?? fallback;
    }

    private static PerformanceBalanceResult ComputeBalance(
        HashSet<Guid> relevantHoldings,
        Dictionary<Guid, List<SnapshotProjection>> snapshotsByHolding,
        DateOnly asOf)
    {
        decimal sum = 0m;
        DateOnly? maxAsOf = null;
        int covered = 0;
        int missing = 0;

        foreach (var holdingId in relevantHoldings)
        {
            SnapshotProjection? latest = null;
            if (snapshotsByHolding.TryGetValue(holdingId, out var list))
            {
                foreach (var s in list)
                {
                    if (s.AsOf <= asOf) { latest = s; break; }
                }
            }

            if (latest is not null && latest.MarketValue.HasValue)
            {
                sum += latest.MarketValue.Value;
                covered++;
                if (maxAsOf is null || latest.AsOf > maxAsOf.Value)
                    maxAsOf = latest.AsOf;
            }
            else
            {
                missing++;
            }
        }

        return new PerformanceBalanceResult(sum, missing == 0, maxAsOf, covered, missing);
    }

    private static string ResolveReportingCurrency(IReadOnlyCollection<SnapshotProjection> snapshots)
    {
        if (snapshots.Count == 0) return DefaultCurrencyCode;

        var mode = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.CurrencyCode))
            .GroupBy(s => s.CurrencyCode!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

        return mode ?? DefaultCurrencyCode;
    }

    private static PortfolioPerformanceResult Empty(DateOnly from, DateOnly to)
    {
        var zero = new PerformanceBalanceResult(0m, true, null, 0, 0);
        return new PortfolioPerformanceResult(from, to, zero, zero, DefaultCurrencyCode);
    }

    private sealed record SnapshotProjection(
        Guid AccountHoldingId,
        DateOnly AsOf,
        decimal? MarketValue,
        string? CurrencyCode);
}
