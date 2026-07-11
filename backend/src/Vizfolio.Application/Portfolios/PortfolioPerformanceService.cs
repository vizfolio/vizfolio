using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

public sealed class PortfolioPerformanceService : IPortfolioPerformanceService
{
    private const string DefaultCurrencyCode = "USD";

    private static readonly TransactionType[] ContributionTypes =
    {
        TransactionType.Deposit,
        TransactionType.Withdrawal,
        TransactionType.Transfer,
    };

    private readonly IAppDbContext _db;
    private readonly ITimeWeightedReturnCalculator _twrCalculator;
    private readonly IMoneyWeightedReturnCalculator _mwrCalculator;

    public PortfolioPerformanceService(
        IAppDbContext db,
        ITimeWeightedReturnCalculator twrCalculator,
        IMoneyWeightedReturnCalculator mwrCalculator)
    {
        _db = db;
        _twrCalculator = twrCalculator;
        _mwrCalculator = mwrCalculator;
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

        var contributionRows = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId)
                        && ContributionTypes.Contains(t.Type)
                        && t.TradeDate >= from
                        && t.TradeDate <= to)
            .Select(t => new { t.TradeDate, t.Amount, t.Type })
            .ToListAsync(cancellationToken);

        var snapshotsByHolding = snapshots
            .GroupBy(s => s.AccountHoldingId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.AsOf).ToList());

        var relevantHoldings = new HashSet<Guid>(snapshotsByHolding.Keys);
        foreach (var id in holdingsActiveInRange) relevantHoldings.Add(id);

        var currency = ResolveReportingCurrency(snapshots);

        var ending = ComputeBalance(relevantHoldings, snapshotsByHolding, to);
        var starting = ComputeBalance(relevantHoldings, snapshotsByHolding, from);

        var contributions = SummarizeContributions(contributionRows.Select(r => new CashFlow(r.TradeDate, r.Amount)).ToList());
        var cashFlows = contributionRows
            .Select(r => new CashFlow(r.TradeDate, r.Amount))
            .OrderBy(f => f.Date)
            .ToList();

        var intermediateBalances = BuildIntermediateBalances(relevantHoldings, snapshotsByHolding, from, to);

        var context = new PerformanceComputationContext(
            From: from,
            To: to,
            StartingBalance: starting.Value,
            StartingIsComplete: starting.IsComplete,
            EndingBalance: ending.Value,
            EndingIsComplete: ending.IsComplete,
            CashFlows: cashFlows,
            IntermediateBalances: intermediateBalances);

        var returns = new PerformanceReturnsResult(
            TimeWeighted: _twrCalculator.Compute(context),
            MoneyWeighted: _mwrCalculator.Compute(context));

        return new PortfolioPerformanceResult(from, to, starting, ending, contributions, returns, currency);
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

    private static List<BalancePoint> BuildIntermediateBalances(
        HashSet<Guid> relevantHoldings,
        Dictionary<Guid, List<SnapshotProjection>> snapshotsByHolding,
        DateOnly from,
        DateOnly to)
    {
        var interiorDates = snapshotsByHolding.Values
            .SelectMany(list => list)
            .Where(s => s.AsOf > from && s.AsOf < to)
            .Select(s => s.AsOf)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var points = new List<BalancePoint>();
        foreach (var date in interiorDates)
        {
            var balance = ComputeBalance(relevantHoldings, snapshotsByHolding, date);
            if (balance.IsComplete && balance.HoldingsCovered > 0)
                points.Add(new BalancePoint(date, balance.Value));
        }
        return points;
    }

    private static PerformanceContributionsResult SummarizeContributions(IReadOnlyList<CashFlow> flows)
    {
        decimal net = 0m, deposits = 0m, withdrawals = 0m;
        foreach (var f in flows)
        {
            net += f.Amount;
            if (f.Amount > 0) deposits += f.Amount;
            else if (f.Amount < 0) withdrawals += f.Amount;
        }
        return new PerformanceContributionsResult(net, deposits, withdrawals, flows.Count);
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
        var noContrib = new PerformanceContributionsResult(0m, 0m, 0m, 0);
        var noReturns = new PerformanceReturnsResult(
            TimeWeighted: new ReturnResult(null, "ModifiedDietz", "Period", "NoData"),
            MoneyWeighted: new ReturnResult(null, "XIRR", "Annualized", "NoData"));
        return new PortfolioPerformanceResult(from, to, zero, zero, noContrib, noReturns, DefaultCurrencyCode);
    }

    private sealed record SnapshotProjection(
        Guid AccountHoldingId,
        DateOnly AsOf,
        decimal? MarketValue,
        string? CurrencyCode);
}
