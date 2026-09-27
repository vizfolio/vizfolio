using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

/// <summary>One share-affecting ledger row used by the quantity roll-forward.</summary>
public readonly record struct LedgerShareEntry(DateOnly TradeDate, TransactionType Type, decimal? Quantity);

/// <summary>
/// Rolls the ledger forward to recover the share quantity a holding had on a given date:
/// <c>Buy + Reinvest + Transfer + reinvested income − Sell</c>, with a <see cref="TransactionType.Split"/> row multiplying the
/// running quantity by its split factor. Splits are <b>CorporateAction-authoritative</b>: a Split row only
/// adjusts the quantity when a matching factor exists for its date (see docs/price-history-valuation.md §5),
/// otherwise it is a logged no-op so an un-sourced split can never silently corrupt the position.
/// </summary>
public static class HoldingQuantityCalculator
{
    /// <summary>Quantity on <paramref name="date"/> rolled forward from zero over the whole ledger.</summary>
    public static decimal QuantityAt(
        IEnumerable<LedgerShareEntry> entries,
        IReadOnlyDictionary<DateOnly, decimal> splitFactorsByExDate,
        DateOnly date,
        Action<DateOnly>? onUnresolvedSplit = null)
        => RollForward(0m, anchorDate: null, entries, splitFactorsByExDate, date, onUnresolvedSplit);

    /// <summary>
    /// Quantity on <paramref name="date"/> rolled forward from a known position: starts at
    /// <paramref name="startingQuantity"/> as of <paramref name="anchorDate"/> (e.g. a broker snapshot, which
    /// already reflects that day's trades) and applies only ledger rows strictly after it. Anchoring on the
    /// broker's quantity keeps a ledger with missing early history from drifting (even negative).
    /// </summary>
    public static decimal RollForward(
        decimal startingQuantity,
        DateOnly? anchorDate,
        IEnumerable<LedgerShareEntry> entries,
        IReadOnlyDictionary<DateOnly, decimal> splitFactorsByExDate,
        DateOnly date,
        Action<DateOnly>? onUnresolvedSplit = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(splitFactorsByExDate);

        var quantity = startingQuantity;
        foreach (var entry in InWindow(entries, anchorDate, date).OrderBy(e => e.TradeDate))
        {
            switch (entry.Type)
            {
                case TransactionType.Buy:
                case TransactionType.Reinvest:
                case TransactionType.Transfer:
                    quantity += entry.Quantity ?? 0m;
                    break;
                case TransactionType.Dividend:
                case TransactionType.CapitalGain:
                    // Older broker reports (e.g. Vanguard pre-2018) record a reinvested distribution as a
                    // single income row carrying the shares bought; a cash distribution has no quantity.
                    quantity += entry.Quantity ?? 0m;
                    break;
                case TransactionType.Sell:
                    // Brokers disagree on the sign of sold units (OFX and the Vanguard report emit
                    // negative UNITS, others positive). A sell always reduces the position.
                    quantity -= Math.Abs(entry.Quantity ?? 0m);
                    break;
                case TransactionType.Split:
                    if (splitFactorsByExDate.TryGetValue(entry.TradeDate, out var factor))
                        quantity *= factor;
                    else
                        onUnresolvedSplit?.Invoke(entry.TradeDate);
                    break;
                default:
                    // Interest, Deposit, Withdrawal, Fee, Other — no share effect.
                    break;
            }
        }

        return quantity;
    }

    /// <summary>True when any share-affecting row falls in <c>(after, date]</c>.</summary>
    public static bool HasActivityBetween(IEnumerable<LedgerShareEntry> entries, DateOnly after, DateOnly date)
        => InWindow(entries, after, date).Any(ChangesShares);

    /// <summary>Income rows only move shares when they carry a reinvested quantity.</summary>
    private static bool ChangesShares(LedgerShareEntry entry)
        => entry.Type is not (TransactionType.Dividend or TransactionType.CapitalGain) || entry.Quantity is not null;

    private static IEnumerable<LedgerShareEntry> InWindow(
        IEnumerable<LedgerShareEntry> entries, DateOnly? after, DateOnly date)
        => entries.Where(e => e.TradeDate <= date && (after is null || e.TradeDate > after.Value));
}
