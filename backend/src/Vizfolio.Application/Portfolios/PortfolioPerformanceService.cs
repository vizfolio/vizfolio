using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Application.Pricing.Abstractions;

namespace Vizfolio.Application.Portfolios;

public sealed class PortfolioPerformanceService : IPortfolioPerformanceService
{
    private readonly IAppDbContext _db;
    private readonly AccountValuationLoader _valuationLoader;
    private readonly ITimeWeightedReturnCalculator _twrCalculator;
    private readonly IMoneyWeightedReturnCalculator _mwrCalculator;
    private readonly IPriceRefreshStatus? _priceRefresh;

    public PortfolioPerformanceService(
        IAppDbContext db,
        AccountValuationLoader valuationLoader,
        ITimeWeightedReturnCalculator twrCalculator,
        IMoneyWeightedReturnCalculator mwrCalculator,
        IPriceRefreshStatus? priceRefresh = null)
    {
        _priceRefresh = priceRefresh;
        _db = db;
        _valuationLoader = valuationLoader;
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
        var to = toInput ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var hasData = accountIds.Count > 0
                      && (await _db.AccountHoldings.AsNoTracking().AnyAsync(h => accountIds.Contains(h.AccountId), cancellationToken)
                          || await _db.AccountTransactions.AsNoTracking().AnyAsync(t => accountIds.Contains(t.AccountId), cancellationToken));
        if (!hasData)
        {
            var resolvedFromEmpty = fromInput ?? to;
            if (resolvedFromEmpty > to) return null;
            return Empty(resolvedFromEmpty, to);
        }

        var from = fromInput ?? await ResolveDefaultFromAsync(accountIds, to, cancellationToken);
        if (from > to) return null;

        // One valuation engine per account values every holding and the account's cash at any date — see
        // docs/price-history-valuation.md §11. Balances, flows and the series all come from it.
        var valuation = await _valuationLoader.LoadAsync(accountIds, to, cancellationToken);
        var engines = valuation.Engines.Values.ToList();
        // The time-weighted return and every chart point value the same dates repeatedly (each flow date's eve, for
        // every point after it), so each date is valued once.
        var balances = new Dictionary<DateOnly, PerformanceBalanceResult>();
        PerformanceBalanceResult ValueAt(DateOnly date)
        {
            if (!balances.TryGetValue(date, out var balance)) balances[date] = balance = BalanceAt(valuation, date);
            return balance;
        }

        var ending = ValueAt(to);
        // The period opens at the *start* of `from` — the close of the previous day — because cash flows
        // dated `from` are counted inside the period. Valuing at the close of `from` would count a
        // first-day purchase twice: once in the starting balance and again as its contribution.
        var starting = ValueAt(from.AddDays(-1));

        var flows = engines.SelectMany(e => e.FlowsBetween(from, to)).OrderBy(f => f.Date).ToList();
        var cashFlows = flows.Select(f => new CashFlow(f.Date, f.Amount)).ToList();
        var contributions = SummarizeContributions(cashFlows);

        var interiorDates = valuation.Snapshots
            .Where(s => s.AsOf > from && s.AsOf < to)
            .Select(s => s.AsOf)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        var intermediateBalances = interiorDates
            .Select(ValueAt)
            .Zip(interiorDates)
            .Where(x => x.First.IsComplete && x.First.HoldingsCovered > 0)
            .Select(x => new BalancePoint(x.Second, x.First.Value))
            .ToList();

        var context = new PerformanceComputationContext(
            From: from,
            To: to,
            StartingBalance: starting.Value,
            StartingIsComplete: starting.IsComplete,
            EndingBalance: ending.Value,
            EndingIsComplete: ending.IsComplete,
            CashFlows: cashFlows,
            IntermediateBalances: intermediateBalances,
            ValueAt: ValueAt);

        // An in-kind transfer that couldn't be valued (no price on the day) leaves the flows unknown, so no return
        // can be computed honestly.
        var unvaluedTransfer = flows.Any(f => !f.IsValued);
        var timeWeighted = _twrCalculator.Compute(context);
        var moneyWeighted = _mwrCalculator.Compute(context);
        if (unvaluedTransfer)
        {
            timeWeighted = ReturnRates.Complete(timeWeighted with { Rate = null, Reason = UnvaluedTransfer }, context.PeriodDays);
            moneyWeighted = ReturnRates.Complete(moneyWeighted with { Rate = null, Reason = UnvaluedTransfer }, context.PeriodDays);
        }

        var returns = new PerformanceReturnsResult(timeWeighted, moneyWeighted);

        // Value / returns-over-time charts, valued by the same engines so they always agree with the
        // balances. Each point's cumulative return is the time-weighted strategy run over [from, point] — for the
        // daily-valued TWR, the running chain of sub-period returns — so the last point equals Returns.TimeWeighted.
        var series = PerformanceSeriesBuilder.Build(
            from,
            to,
            ValueAt,
            cashFlows,
            (date, balance) => unvaluedTransfer ? null : _twrCalculator.Compute(context with
            {
                To = date,
                EndingBalance = balance.Value,
                EndingIsComplete = balance.IsComplete,
                CashFlows = cashFlows.Where(f => f.Date <= date).ToList(),
                IntermediateBalances = intermediateBalances.Where(b => b.Date < date).ToList(),
            }).Rate);

        return new PortfolioPerformanceResult(
            from, to, starting, ending, contributions, returns, valuation.ReportingCurrency, series);
    }

    private const string UnvaluedTransfer = "UnvaluedTransfer";

    private async Task<DateOnly> ResolveDefaultFromAsync(
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
            .Where(s => _db.AccountHoldings.Any(h => h.AccountHoldingId == s.AccountHoldingId && accountIds.Contains(h.AccountId)))
            .OrderBy(s => s.AsOf)
            .Select(s => (DateOnly?)s.AsOf)
            .FirstOrDefaultAsync(cancellationToken);
        return earliestSnapshot ?? fallback;
    }

    /// <summary>The value of every loaded account at the close of <paramref name="date"/>.</summary>
    private PerformanceBalanceResult BalanceAt(LoadedValuation valuation, DateOnly date)
    {
        decimal sum = 0m;
        DateOnly? maxAsOf = null;
        int covered = 0;
        var missing = new List<MissingValuation>();
        var missingCount = 0;

        foreach (var account in valuation.Engines)
        {
            var value = account.Value.ValueAt(date);
            sum += value.Value;
            foreach (var component in value.Components)
            {
                switch (component.Status)
                {
                    case HoldingValuationStatus.Covered:
                        covered++;
                        if (component.SnapshotAsOf is { } d && (maxAsOf is null || d > maxAsOf.Value)) maxAsOf = d;
                        break;
                    case HoldingValuationStatus.Missing:
                        missingCount++;
                        if (missing.Count < PerformanceBalanceResult.MaxMissingDetails)
                            missing.Add(new MissingValuation(
                                account.Key, component.HoldingId, component.Symbol,
                                MissingCauseNames.Of(component.Cause, _priceRefresh?.IsPending(account.Key) == true)));
                        break;
                }
            }
        }

        return new PerformanceBalanceResult(sum, missingCount == 0, maxAsOf, covered, missingCount) { Missing = missing };
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

    private static PortfolioPerformanceResult Empty(DateOnly from, DateOnly to)
    {
        var zero = new PerformanceBalanceResult(0m, true, null, 0, 0);
        var noContrib = new PerformanceContributionsResult(0m, 0m, 0m, 0);
        var noReturns = new PerformanceReturnsResult(
            TimeWeighted: new ReturnResult(null, DailyValuedTimeWeightedReturnCalculator.MethodName, ReturnResult.PeriodBasis, "NoData"),
            MoneyWeighted: new ReturnResult(null, "XIRR", ReturnResult.AnnualizedBasis, "NoData"));
        return new PortfolioPerformanceResult(
            from, to, zero, zero, noContrib, noReturns, AccountValuationLoader.DefaultCurrencyCode, PerformanceSeriesResult.Empty);
    }
}
