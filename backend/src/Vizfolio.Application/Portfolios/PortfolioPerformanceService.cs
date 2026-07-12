using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

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

    private static readonly TransactionType[] ShareAffectingTypes =
    {
        TransactionType.Buy,
        TransactionType.Sell,
        TransactionType.Reinvest,
        TransactionType.Transfer,
        TransactionType.Split,
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
            .Select(s => new SnapshotProjection(s.AccountHoldingId, s.AsOf, s.MarketValue, s.CurrencyCode, s.Quantity))
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

        // Resolve each holding's value from PriceHistory (quantity × price) with a snapshot fallback,
        // so windowed balances are honest at any date — see docs/price-history-valuation.md §4.
        var resolver = await BuildResolverAsync(
            relevantHoldings, snapshotsByHolding, currency, to, cancellationToken);

        var ending = ComputeBalance(relevantHoldings, resolver, to);
        var starting = ComputeBalance(relevantHoldings, resolver, from);

        var contributions = SummarizeContributions(contributionRows.Select(r => new CashFlow(r.TradeDate, r.Amount)).ToList());
        var cashFlows = contributionRows
            .Select(r => new CashFlow(r.TradeDate, r.Amount))
            .OrderBy(f => f.Date)
            .ToList();

        var interiorDates = snapshots
            .Where(s => s.AsOf > from && s.AsOf < to)
            .Select(s => s.AsOf)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        var intermediateBalances = BuildIntermediateBalances(relevantHoldings, resolver, interiorDates);

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
        HoldingValuationResolver resolver,
        DateOnly asOf)
    {
        decimal sum = 0m;
        DateOnly? maxAsOf = null;
        int covered = 0;
        int missing = 0;

        foreach (var holdingId in relevantHoldings)
        {
            var valuation = resolver.Resolve(holdingId, asOf);
            switch (valuation.Status)
            {
                case HoldingValuationStatus.Covered:
                    sum += valuation.Value;
                    covered++;
                    if (valuation.SnapshotAsOf is { } d && (maxAsOf is null || d > maxAsOf.Value))
                        maxAsOf = d;
                    break;
                case HoldingValuationStatus.NotHeld:
                    // A true $0 (not held on this date): contributes nothing and is complete.
                    break;
                case HoldingValuationStatus.Missing:
                    missing++;
                    break;
            }
        }

        return new PerformanceBalanceResult(sum, missing == 0, maxAsOf, covered, missing);
    }

    private static List<BalancePoint> BuildIntermediateBalances(
        HashSet<Guid> relevantHoldings,
        HoldingValuationResolver resolver,
        IReadOnlyList<DateOnly> interiorDates)
    {
        var points = new List<BalancePoint>();
        foreach (var date in interiorDates)
        {
            var balance = ComputeBalance(relevantHoldings, resolver, date);
            if (balance.IsComplete && balance.HoldingsCovered > 0)
                points.Add(new BalancePoint(date, balance.Value));
        }
        return points;
    }

    private async Task<HoldingValuationResolver> BuildResolverAsync(
        HashSet<Guid> relevantHoldings,
        Dictionary<Guid, List<SnapshotProjection>> snapshotsByHolding,
        string reportingCurrency,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (relevantHoldings.Count == 0)
            return new HoldingValuationResolver(new Dictionary<Guid, HoldingValuationData>());

        var holdingIds = relevantHoldings.ToList();

        var seriesRows = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => holdingIds.Contains(h.AccountHoldingId))
            .Select(h => new { h.AccountHoldingId, h.SecurityId, h.Symbol })
            .ToListAsync(cancellationToken);

        var securityIds = seriesRows
            .Where(r => r.SecurityId != null)
            .Select(r => r.SecurityId!.Value)
            .Distinct()
            .ToList();
        var symbolKeys = seriesRows
            .Where(r => r.SecurityId == null && r.Symbol != null)
            .Select(r => r.Symbol!.ToUpperInvariant())
            .Distinct()
            .ToList();

        var ledgerRows = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountHoldingId != null
                        && holdingIds.Contains(t.AccountHoldingId!.Value)
                        && t.TradeDate <= to
                        && ShareAffectingTypes.Contains(t.Type))
            .Select(t => new { HoldingId = t.AccountHoldingId!.Value, t.TradeDate, t.Type, t.Quantity })
            .ToListAsync(cancellationToken);
        var ledgerByHolding = ledgerRows
            .GroupBy(r => r.HoldingId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<LedgerShareEntry>)g
                    .Select(r => new LedgerShareEntry(r.TradeDate, r.Type, r.Quantity))
                    .ToList());

        // Prices/splits are shared reference series keyed by security or symbol. Filter prices to the
        // reporting currency (null currency — e.g. from the keyless source — is treated as matching).
        var priceRows = await _db.PriceHistories
            .AsNoTracking()
            .Where(p => p.AsOf <= to
                        && (p.CurrencyCode == null || p.CurrencyCode == reportingCurrency)
                        && ((p.SecurityId != null && securityIds.Contains(p.SecurityId!.Value))
                            || (p.SymbolKey != null && symbolKeys.Contains(p.SymbolKey!))))
            .Select(p => new { p.SecurityId, p.SymbolKey, p.AsOf, p.Close })
            .ToListAsync(cancellationToken);
        var pricesBySecurity = priceRows
            .Where(p => p.SecurityId != null)
            .GroupBy(p => p.SecurityId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.AsOf)
                .Select(p => new PricePointData(p.AsOf, p.Close)).ToList());
        var pricesBySymbol = priceRows
            .Where(p => p.SymbolKey != null)
            .GroupBy(p => p.SymbolKey!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.AsOf)
                .Select(p => new PricePointData(p.AsOf, p.Close)).ToList());

        var splitRows = await _db.CorporateActions
            .AsNoTracking()
            .Where(c => c.Type == CorporateActionType.Split
                        && c.ExDate <= to
                        && ((c.SecurityId != null && securityIds.Contains(c.SecurityId!.Value))
                            || (c.SymbolKey != null && symbolKeys.Contains(c.SymbolKey!))))
            .Select(c => new { c.SecurityId, c.SymbolKey, c.ExDate, c.SplitNumerator, c.SplitDenominator })
            .ToListAsync(cancellationToken);
        var splitsBySecurity = splitRows
            .Where(c => c.SecurityId != null)
            .GroupBy(c => c.SecurityId!.Value)
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.ExDate, c => c.SplitNumerator / c.SplitDenominator));
        var splitsBySymbol = splitRows
            .Where(c => c.SymbolKey != null)
            .GroupBy(c => c.SymbolKey!)
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.ExDate, c => c.SplitNumerator / c.SplitDenominator));

        var emptyPrices = (IReadOnlyList<PricePointData>)Array.Empty<PricePointData>();
        var emptySplits = (IReadOnlyDictionary<DateOnly, decimal>)new Dictionary<DateOnly, decimal>();
        var emptyLedger = (IReadOnlyList<LedgerShareEntry>)Array.Empty<LedgerShareEntry>();

        var byHolding = new Dictionary<Guid, HoldingValuationData>();
        foreach (var row in seriesRows)
        {
            var symbolKey = row.Symbol?.ToUpperInvariant();
            var prices = emptyPrices;
            var splits = emptySplits;
            if (row.SecurityId is { } sid)
            {
                if (pricesBySecurity.TryGetValue(sid, out var pl)) prices = pl;
                if (splitsBySecurity.TryGetValue(sid, out var sd)) splits = sd;
            }
            else if (symbolKey != null)
            {
                if (pricesBySymbol.TryGetValue(symbolKey, out var pl)) prices = pl;
                if (splitsBySymbol.TryGetValue(symbolKey, out var sd)) splits = sd;
            }

            var ledger = ledgerByHolding.TryGetValue(row.AccountHoldingId, out var le) ? le : emptyLedger;
            var snaps = snapshotsByHolding.TryGetValue(row.AccountHoldingId, out var sp)
                ? sp.Select(s => new SnapshotData(s.AsOf, s.Quantity, s.MarketValue)).ToList()
                : new List<SnapshotData>();

            byHolding[row.AccountHoldingId] = new HoldingValuationData
            {
                Ledger = ledger,
                SplitFactors = splits,
                PricesDescending = prices,
                SnapshotsDescending = snaps,
            };
        }

        return new HoldingValuationResolver(byHolding);
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
        string? CurrencyCode,
        decimal Quantity);
}
