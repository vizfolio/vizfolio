using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

/// <summary>One ledger row as the cash roll sees it.</summary>
public readonly record struct CashLedgerRow(
    DateOnly TradeDate,
    TransactionType Type,
    decimal Amount,
    decimal? Quantity,
    string? Ticker,
    string? SourceType,
    DateOnly? SettlementDate = null,
    bool IsSettlementFund = false)
{
    /// <summary>
    /// When the cash actually moves: at settlement, not on the trade date. A purchase settles days after
    /// it trades, and the money funding it is often recorded on its (later) settlement date too.
    /// </summary>
    public DateOnly CashDate => SettlementDate ?? TradeDate;
}

/// <summary>A ledger row paired with how it moved the account's cash.</summary>
public readonly record struct CashMovement(CashLedgerRow Row, decimal CashEffect);

/// <summary>
/// A contribution the ledger implies but never recorded: on <see cref="Date"/> the account spent more cash
/// than it had, so <see cref="Amount"/> must have come from outside the account.
/// </summary>
public sealed record ImpliedContribution(
    DateOnly Date,
    decimal Amount,
    decimal CashBefore,
    IReadOnlyList<CashMovement> DayRows);

/// <summary>
/// Finds purchases funded from outside the account when the import has no matching deposit — typical of
/// older fund-company (pre-brokerage) accounts, where a contribution <i>was</i> a purchase and the history
/// only shows the Buy. It rolls the account's cash forward from zero, one day at a time by the date cash
/// actually moves (<see cref="CashLedgerRow.CashDate"/>, i.e. settlement; row order within a day doesn't
/// matter); whenever a day closes below zero by more than the tolerance, the shortfall is an implied
/// contribution and cash is reset to zero.
/// <para>
/// The brokerage <b>settlement fund</b> (e.g. a money-market sweep vehicle) counts as cash: moving money
/// in or out of it doesn't change what's available to spend, only its income does. It is recognised from
/// the account's own data — any ticker on a "Sweep" row — which also keeps two sources recording the same
/// sweep differently (one as a Buy of the fund, the other as a "Sweep in") from counting it twice.
/// </para>
/// </summary>
public static class ImpliedContributionCalculator
{
    /// <summary>Shortfalls at or below this (rounding cents, interest timing) are ignored.</summary>
    public const decimal DefaultTolerance = 1m;

    /// <param name="rows">The account's imported rows.</param>
    /// <param name="tolerance">Shortfalls at or below this are ignored.</param>
    /// <param name="openingCash">
    /// Cash already in the account before its first row — non-zero only when the imported history is partial and
    /// the broker's statements show cash held before it (see AccountStateEngine.OpeningCashSeed). Purchases paid
    /// from it are not outside money.
    /// </param>
    public static IReadOnlyList<ImpliedContribution> Find(
        IEnumerable<CashLedgerRow> rows, decimal tolerance = DefaultTolerance, decimal openingCash = 0m)
        => Roll(rows, tolerance, openingCash).Contributions;

    /// <summary>The cash the ledger implies is left in the account after all rows (with implied top-ups).</summary>
    public static decimal EndingCash(
        IEnumerable<CashLedgerRow> rows, decimal tolerance = DefaultTolerance, decimal openingCash = 0m)
        => Roll(rows, tolerance, openingCash).Cash;

    /// <summary>
    /// Tickers the account treats as its settlement fund: any ticker on a row the broker's parser marked as a
    /// settlement-fund movement (see the broker profiles in docs/performance-api.md), or — for rows stored before
    /// parsers marked them — on a row labelled "Sweep…".
    /// </summary>
    public static IReadOnlySet<string> SettlementTickers(IEnumerable<CashLedgerRow> rows)
        => rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Ticker)
                        && (r.IsSettlementFund
                            || r.SourceType?.TrimStart().StartsWith("sweep", StringComparison.OrdinalIgnoreCase) == true))
            .Select(r => r.Ticker!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// How a row moves the account's cash, independent of each broker's sign conventions (older Vanguard
    /// reports, for example, record a fund purchase as a positive amount).
    /// </summary>
    public static decimal CashEffect(CashLedgerRow row, IReadOnlySet<string> settlementTickers)
    {
        if (IsSettlementFund(row, settlementTickers))
        {
            // An older fund-company account records new money as a purchase of the money-market fund itself,
            // with a positive amount (that era's sign for every purchase) and no deposit row. That is money from
            // outside, so it spends cash like any purchase and surfaces as an implied contribution. A sweep into
            // the fund from cash is reported with a negative amount (or no quantity) and stays neutral.
            if (row.Type == TransactionType.Buy && row.Amount > 0m && row.Quantity is > 0m)
                return -row.Amount;

            // Cash ↔ settlement fund is cash ↔ cash; only its income adds to what the account can spend.
            return row.Type switch
            {
                TransactionType.Dividend or TransactionType.Interest or TransactionType.CapitalGain => row.Amount,
                TransactionType.ReturnOfCapital => Math.Abs(row.Amount),
                _ => 0m,
            };
        }

        return CashEffect(row);
    }

    private static decimal CashEffect(CashLedgerRow row) => row.Type switch
    {
        TransactionType.Deposit => Math.Abs(row.Amount),
        TransactionType.Withdrawal => -Math.Abs(row.Amount),
        TransactionType.Buy or TransactionType.Reinvest or TransactionType.Fee => -Math.Abs(row.Amount),
        TransactionType.Sell => Math.Abs(row.Amount),

        // An in-kind transfer moves shares, not cash; a cash transfer is signed by direction at import.
        TransactionType.Transfer => IsInKind(row) ? 0m : row.Amount,

        // Income that carries a quantity was reinvested at source (no cash); otherwise it's paid in cash.
        TransactionType.Dividend or TransactionType.Interest or TransactionType.CapitalGain =>
            row.Quantity is null ? row.Amount : 0m,

        // Part of the investment paid back: cash in, but neither income nor a contribution.
        TransactionType.ReturnOfCapital => Math.Abs(row.Amount),

        // A split moves shares only; any amount is cash paid for fractional shares.
        TransactionType.Split => row.Amount,

        // Unrecognised broker activity is taken at its reported cash sign.
        TransactionType.Other => row.Amount,

        _ => 0m, // Journal: between the account's own sub-accounts
    };

    /// <summary>
    /// A reinvestment is paid for by the income it reinvests. Some sources list that income as its own row
    /// the same day (the Vanguard report: Dividend +, Reinvestment −, netting to zero); others fold it into
    /// the reinvestment row itself (a QFX <c>REINVEST</c> has no separate income row). The part of a day's
    /// reinvestments not covered by that day's cash income is therefore income the file doesn't list, and
    /// is added back so a reinvestment never looks like an unfunded purchase. Settled per day rather than
    /// per ticker because some reports leave the income row's ticker blank; settlement-fund rows are
    /// already cash-neutral and are left out.
    /// </summary>
    private static decimal SelfFundedReinvestment(IEnumerable<CashLedgerRow> day, IReadOnlySet<string> settlementTickers)
    {
        var rows = day.Where(r => !IsSettlementFund(r, settlementTickers)).ToList();
        var reinvested = rows.Where(r => r.Type == TransactionType.Reinvest).Sum(r => Math.Abs(r.Amount));
        if (reinvested == 0m) return 0m;

        var cashIncome = rows
            .Where(r => r.Type is TransactionType.Dividend or TransactionType.Interest or TransactionType.CapitalGain
                        && r.Quantity is null)
            .Sum(r => r.Amount);

        return Math.Max(reinvested - Math.Max(cashIncome, 0m), 0m);
    }

    private static bool IsSettlementFund(CashLedgerRow row, IReadOnlySet<string> settlementTickers)
        => !string.IsNullOrWhiteSpace(row.Ticker) && settlementTickers.Contains(row.Ticker.Trim().ToUpperInvariant());

    private static bool IsInKind(CashLedgerRow row)
        => !string.IsNullOrWhiteSpace(row.Ticker) && row.Quantity is { } q && q != 0m;

    private static (IReadOnlyList<ImpliedContribution> Contributions, decimal Cash) Roll(
        IEnumerable<CashLedgerRow> rows, decimal tolerance, decimal openingCash)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var all = rows.ToList();
        var settlementTickers = SettlementTickers(all);

        var contributions = new List<ImpliedContribution>();
        var cash = openingCash;
        foreach (var day in all.GroupBy(r => r.CashDate).OrderBy(g => g.Key))
        {
            var movements = day.Select(r => new CashMovement(r, CashEffect(r, settlementTickers))).ToList();
            var cashBefore = cash;
            cash += movements.Sum(m => m.CashEffect) + SelfFundedReinvestment(day, settlementTickers);
            if (cash >= -tolerance) { continue; }

            contributions.Add(new ImpliedContribution(day.Key, -cash, cashBefore, movements));
            cash = 0m;
        }

        return (contributions, cash);
    }
}
