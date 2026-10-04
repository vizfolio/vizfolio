using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// How ledger rows move an account's <b>cash</b> — uninvested cash plus the settlement fund, which is the same
/// money — for valuation. Builds on <see cref="ImpliedContributionCalculator.CashEffect(CashLedgerRow, IReadOnlySet{string})"/>
/// (broker-agnostic signs, in-kind transfers, cash vs. reinvested income) with the settlement fund folded in:
/// <list type="bullet">
///   <item>sweeps between cash and the settlement fund ("Sweep…" rows, whatever their ticker) move nothing;</item>
///   <item>buying, selling or reinvesting into the settlement fund moves nothing (it's cash either way). An old
///   fund-company purchase of the fund with outside money is covered by its implied contribution row;</item>
///   <item>the settlement fund's income, and transfers of it, move cash;</item>
///   <item>a reinvestment with no security (some reports omit the settlement fund's ticker) keeps the money in cash.</item>
/// </list>
/// Reinvestments are funded by the income they reinvest; when a source reports only the reinvestment (a QFX
/// <c>REINVEST</c>), the income is added back per day, as the implied-contribution roll does.
/// </summary>
public static class AccountCash
{
    private static readonly IReadOnlySet<string> NoSettlement = new HashSet<string>();

    /// <summary>The net cash movement of one day's rows.</summary>
    public static decimal DayEffect(IReadOnlyCollection<LedgerRow> day, IReadOnlySet<string> settlement)
        => day.Sum(r => Effect(r, settlement))
           + ReinvestedIncomeNotListed(day, settlement);

    /// <summary>One row's cash movement (before the per-day reinvestment add-back).</summary>
    public static decimal Effect(LedgerRow row, IReadOnlySet<string> settlement)
    {
        if (row.IsSweep) return 0m;

        if (SettlementFunds.IsSettlement(row.Ticker, settlement))
        {
            return row.Type switch
            {
                TransactionType.Dividend or TransactionType.Interest or TransactionType.CapitalGain => row.Amount,
                TransactionType.ReturnOfCapital => Math.Abs(row.Amount),
                TransactionType.Transfer => row.Amount,
                TransactionType.Deposit => Math.Abs(row.Amount),
                TransactionType.Withdrawal => -Math.Abs(row.Amount),
                TransactionType.Fee => -Math.Abs(row.Amount),
                _ => 0m, // Buy / Sell / Reinvest / Other: cash ↔ settlement fund
            };
        }

        if (row.Type == TransactionType.Reinvest && string.IsNullOrWhiteSpace(row.Ticker) && row.HoldingId is null)
            return 0m;

        // An in-kind transfer moves shares of a holding, not cash (even when the source left the ticker blank).
        if (row.Type == TransactionType.Transfer && row.HoldingId is not null && row.Quantity is { } q && q != 0m)
            return 0m;

        return ImpliedContributionCalculator.CashEffect(ToCashRow(row), NoSettlement);
    }

    /// <summary>
    /// Income reinvested on a day whose income row the source doesn't list. For ordinary funds the reinvestment
    /// spent cash (−) and the income never arrived, so the uncovered part is added back. For the settlement fund
    /// the reinvestment moved nothing, so its uncovered income is added. Sources are compared rather than summed
    /// (the larger total per source wins), because two overlapping imports can each report the same reinvestment.
    /// </summary>
    private static decimal ReinvestedIncomeNotListed(IReadOnlyCollection<LedgerRow> day, IReadOnlySet<string> settlement)
    {
        decimal addBack = 0m;

        var ordinary = day
            .Where(r => !r.IsSweep && !SettlementFunds.IsSettlement(r.Ticker, settlement))
            .ToList();
        var reinvested = ordinary
            .Where(r => r.Type == TransactionType.Reinvest && (r.HoldingId is not null || !string.IsNullOrWhiteSpace(r.Ticker)))
            .Sum(r => Math.Abs(r.Amount));
        if (reinvested > 0m)
        {
            var cashIncome = ordinary.Where(IsCashIncome).Sum(r => r.Amount);
            addBack += Math.Max(reinvested - Math.Max(cashIncome, 0m), 0m);
        }

        var settled = day.Where(r => !r.IsSweep && SettlementFunds.IsSettlement(r.Ticker, settlement)).ToList();
        var settledReinvested = LargestPerSource(settled.Where(r => r.Type == TransactionType.Reinvest));
        if (settledReinvested > 0m)
        {
            var settledIncome = settled
                .Where(r => r.Type is TransactionType.Dividend or TransactionType.Interest or TransactionType.CapitalGain)
                .Sum(r => r.Amount);
            addBack += Math.Max(settledReinvested - Math.Max(settledIncome, 0m), 0m);
        }

        return addBack;
    }

    private static decimal LargestPerSource(IEnumerable<LedgerRow> rows)
        => rows.GroupBy(r => r.SourceSystem).Select(g => g.Sum(r => Math.Abs(r.Amount))).DefaultIfEmpty(0m).Max();

    private static bool IsCashIncome(LedgerRow r)
        => r.Type is TransactionType.Dividend or TransactionType.Interest or TransactionType.CapitalGain && r.Quantity is null;

    internal static CashLedgerRow ToCashRow(LedgerRow r)
        => new(r.TradeDate, r.Type, r.Amount, r.Quantity, r.Ticker, r.SourceType, r.SettlementDate, r.IsSettlementFund);
}
