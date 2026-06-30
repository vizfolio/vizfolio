using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Performance.RateOfReturn;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Performance;

public sealed class PerformanceCalculator : IPerformanceCalculator
{
    private const string DefaultCurrency = "USD";

    private readonly IAppDbContext _db;

    public PerformanceCalculator(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PerformanceResult?> CalculateAsync(PerformanceRequest request, CancellationToken ct)
    {
        var accountIds = await ResolveAccountIdsAsync(request, ct);
        if (accountIds is null)
            return null;

        var holdings = await _db.AccountHoldings.AsNoTracking()
            .Where(h => accountIds.Contains(h.AccountId))
            .Select(h => new HoldingInfo(h.AccountHoldingId, h.Kind, h.CurrencyCode))
            .ToListAsync(ct);

        var holdingIds = holdings.Select(h => h.HoldingId).ToList();

        var snapshotRows = await _db.AccountHoldingSnapshots.AsNoTracking()
            .Where(s => holdingIds.Contains(s.AccountHoldingId) && s.AsOf <= request.To)
            .Select(s => new SnapshotInfo(
                s.AccountHoldingId, s.AsOf, s.Quantity, s.MarketValue, s.UnitPrice))
            .ToListAsync(ct);

        var snapshotsByHolding = snapshotRows
            .GroupBy(s => s.HoldingId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.AsOf).ToList());

        var allTransactions = await _db.AccountTransactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.TradeDate <= request.To)
            .Select(t => new TxInfo(t.AccountId, t.TradeDate, t.Type, t.Amount))
            .ToListAsync(ct);

        var notes = new List<string>();
        var balanceAt = BuildBalanceFunc(holdings, snapshotsByHolding, allTransactions);

        var beginningBalance = balanceAt(request.From.AddDays(-1));
        var endingBalance = balanceAt(request.To);

        var (deposits, withdrawals, rorFlows) = ClassifyCashFlows(
            allTransactions, request.From, request.To, request.Scope, notes);

        var netCashFlow = deposits - withdrawals;
        var investmentReturn = endingBalance - beginningBalance - netCashFlow;

        var simple = SimpleReturnCalculator.Calculate(beginningBalance, endingBalance, netCashFlow);
        var dietz = ModifiedDietzCalculator.Calculate(
            beginningBalance, endingBalance, request.From, request.To.AddDays(1), rorFlows);
        var twr = TimeWeightedReturnCalculator.Calculate(
            beginningBalance, endingBalance, request.From, request.To, rorFlows, balanceAt);
        var irr = InternalRateOfReturnCalculator.Calculate(
            beginningBalance, endingBalance, request.From, request.To, rorFlows);

        var series = BuildSeries(request, beginningBalance, balanceAt, rorFlows);

        var currency = ResolveCurrency(holdings, notes);

        return new PerformanceResult(
            request.Scope,
            request.ScopeId,
            currency,
            request.From,
            request.To,
            request.Granularity,
            new PerformanceSummary(
                beginningBalance,
                endingBalance,
                deposits,
                withdrawals,
                netCashFlow,
                investmentReturn,
                new RateOfReturnResult(dietz, simple, twr, irr, notes)),
            series);
    }

    private async Task<HashSet<Guid>?> ResolveAccountIdsAsync(PerformanceRequest request, CancellationToken ct)
    {
        if (request.Scope == PerformanceScope.Portfolio)
        {
            var exists = await _db.Portfolios.AsNoTracking()
                .AnyAsync(p => p.PortfolioId == request.ScopeId, ct);
            if (!exists)
                return null;

            var ids = await _db.Accounts.AsNoTracking()
                .Where(a => a.PortfolioId == request.ScopeId)
                .Select(a => a.AccountId)
                .ToListAsync(ct);
            return ids.ToHashSet();
        }
        else
        {
            var exists = await _db.Accounts.AsNoTracking()
                .AnyAsync(a => a.AccountId == request.ScopeId, ct);
            if (!exists)
                return null;

            return new HashSet<Guid> { request.ScopeId };
        }
    }

    private static Func<DateOnly, decimal> BuildBalanceFunc(
        IReadOnlyList<HoldingInfo> holdings,
        Dictionary<Guid, List<SnapshotInfo>> snapshotsByHolding,
        IReadOnlyList<TxInfo> transactions)
    {
        var nonCashHoldings = holdings.Where(h => h.Kind != AccountHoldingKind.Cash).ToList();
        var orderedTx = transactions.OrderBy(t => t.TradeDate).ToList();

        return asOf =>
        {
            decimal value = 0m;
            foreach (var h in nonCashHoldings)
            {
                if (!snapshotsByHolding.TryGetValue(h.HoldingId, out var snaps))
                    continue;
                SnapshotInfo? latest = null;
                foreach (var s in snaps)
                {
                    if (s.AsOf > asOf) break;
                    latest = s;
                }
                if (latest is null) continue;

                if (latest.MarketValue.HasValue)
                    value += latest.MarketValue.Value;
                else if (latest.UnitPrice.HasValue)
                    value += latest.Quantity * latest.UnitPrice.Value;
            }

            foreach (var t in orderedTx)
            {
                if (t.TradeDate > asOf) break;
                value += t.Amount;
            }
            return value;
        };
    }

    private static (decimal Deposits, decimal Withdrawals, IReadOnlyList<CashFlow> Flows) ClassifyCashFlows(
        IReadOnlyList<TxInfo> allTransactions,
        DateOnly from,
        DateOnly to,
        PerformanceScope scope,
        List<string> notes)
    {
        var inRange = allTransactions
            .Where(t => t.TradeDate >= from && t.TradeDate <= to)
            .Where(t => t.Type == TransactionType.Deposit
                     || t.Type == TransactionType.Withdrawal
                     || t.Type == TransactionType.Transfer)
            .ToList();

        // For portfolio scope, drop intra-portfolio transfer pairs that net to zero on the same date.
        if (scope == PerformanceScope.Portfolio)
        {
            var transferGroups = inRange
                .Where(t => t.Type == TransactionType.Transfer)
                .GroupBy(t => (t.TradeDate, Magnitude: Math.Abs(t.Amount)))
                .ToList();

            var toDrop = new HashSet<TxInfo>();
            var anyMatched = false;
            foreach (var g in transferGroups)
            {
                var list = g.ToList();
                if (list.Count < 2) continue;
                if (list.Sum(t => t.Amount) == 0m)
                {
                    foreach (var t in list) toDrop.Add(t);
                    anyMatched = true;
                }
            }
            if (anyMatched)
                notes.Add("Transfer pairs that net to zero on the same date were treated as intra-portfolio movements and excluded from cash flows.");

            inRange = inRange.Where(t => !toDrop.Contains(t)).ToList();
        }

        decimal deposits = 0m;
        decimal withdrawals = 0m;
        var flows = new List<CashFlow>(inRange.Count);

        foreach (var tx in inRange)
        {
            decimal signed = tx.Type switch
            {
                TransactionType.Deposit => Math.Abs(tx.Amount),
                TransactionType.Withdrawal => -Math.Abs(tx.Amount),
                TransactionType.Transfer => tx.Amount,
                _ => 0m
            };

            if (signed > 0) deposits += signed;
            else if (signed < 0) withdrawals += -signed;

            if (signed != 0m)
                flows.Add(new CashFlow(tx.TradeDate, signed));
        }

        return (deposits, withdrawals, flows);
    }

    private static IReadOnlyList<PerformancePeriod> BuildSeries(
        PerformanceRequest request,
        decimal beginningBalance,
        Func<DateOnly, decimal> balanceAt,
        IReadOnlyList<CashFlow> rorFlows)
    {
        var periods = BuildPeriods(request.From, request.To, request.Granularity);
        var items = new List<PerformancePeriod>(periods.Count);
        var prevBalance = beginningBalance;

        foreach (var (start, end) in periods)
        {
            var balAtEnd = balanceAt(end);

            decimal periodDeposits = 0m;
            decimal periodWithdrawals = 0m;
            foreach (var f in rorFlows)
            {
                if (f.Date < start || f.Date > end) continue;
                if (f.Amount > 0) periodDeposits += f.Amount;
                else periodWithdrawals += -f.Amount;
            }
            var periodNetFlow = periodDeposits - periodWithdrawals;
            var periodReturn = balAtEnd - prevBalance - periodNetFlow;

            var cumFlows = rorFlows.Where(f => f.Date >= request.From && f.Date <= end).ToList();
            var cumulative = ModifiedDietzCalculator.Calculate(
                beginningBalance, balAtEnd, request.From, end.AddDays(1), cumFlows);

            items.Add(new PerformancePeriod(
                start, end, balAtEnd,
                periodDeposits, periodWithdrawals, periodNetFlow, periodReturn, cumulative));

            prevBalance = balAtEnd;
        }

        return items;
    }

    private static List<(DateOnly Start, DateOnly End)> BuildPeriods(
        DateOnly from, DateOnly to, PerformanceGranularity granularity)
    {
        var periods = new List<(DateOnly Start, DateOnly End)>();
        var cursor = from;
        while (cursor <= to)
        {
            DateOnly bucketEnd = granularity switch
            {
                PerformanceGranularity.Daily => cursor,
                PerformanceGranularity.Weekly => cursor.AddDays(6),
                PerformanceGranularity.Monthly => new DateOnly(
                    cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month)),
                PerformanceGranularity.Quarterly => EndOfQuarter(cursor),
                PerformanceGranularity.Yearly => new DateOnly(cursor.Year, 12, 31),
                _ => cursor
            };
            if (bucketEnd > to) bucketEnd = to;
            periods.Add((cursor, bucketEnd));
            cursor = bucketEnd.AddDays(1);
        }
        return periods;
    }

    private static DateOnly EndOfQuarter(DateOnly d)
    {
        var quarterIndex = (d.Month - 1) / 3;
        var lastMonth = (quarterIndex + 1) * 3;
        return new DateOnly(d.Year, lastMonth, DateTime.DaysInMonth(d.Year, lastMonth));
    }

    private static string ResolveCurrency(IReadOnlyList<HoldingInfo> holdings, List<string> notes)
    {
        var grouped = holdings
            .Where(h => !string.IsNullOrWhiteSpace(h.CurrencyCode))
            .GroupBy(h => h.CurrencyCode!)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        if (grouped.Count == 0)
            return DefaultCurrency;

        if (grouped.Count > 1)
            notes.Add($"Holdings span multiple currencies; values shown in {grouped[0].Code} without FX conversion.");

        return grouped[0].Code;
    }

    private sealed record HoldingInfo(Guid HoldingId, AccountHoldingKind Kind, string? CurrencyCode);

    private sealed record SnapshotInfo(
        Guid HoldingId, DateOnly AsOf, decimal Quantity, decimal? MarketValue, decimal? UnitPrice);

    private sealed record TxInfo(Guid AccountId, DateOnly TradeDate, TransactionType Type, decimal Amount);
}
