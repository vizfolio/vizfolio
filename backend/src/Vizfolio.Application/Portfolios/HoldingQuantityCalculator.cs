using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

/// <summary>One share-affecting ledger row used by the quantity roll-forward.</summary>
public readonly record struct LedgerShareEntry(DateOnly TradeDate, TransactionType Type, decimal? Quantity);

/// <summary>
/// Rolls the ledger forward to recover the share quantity a holding had on a given date:
/// <c>Buy + Reinvest + Transfer − Sell</c>, with a <see cref="TransactionType.Split"/> row multiplying the
/// running quantity by its split factor. Splits are <b>CorporateAction-authoritative</b>: a Split row only
/// adjusts the quantity when a matching factor exists for its date (see docs/price-history-valuation.md §5),
/// otherwise it is a logged no-op so an un-sourced split can never silently corrupt the position.
/// </summary>
public static class HoldingQuantityCalculator
{
    public static decimal QuantityAt(
        IEnumerable<LedgerShareEntry> entries,
        IReadOnlyDictionary<DateOnly, decimal> splitFactorsByExDate,
        DateOnly date,
        Action<DateOnly>? onUnresolvedSplit = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(splitFactorsByExDate);

        var quantity = 0m;
        foreach (var entry in entries.Where(e => e.TradeDate <= date).OrderBy(e => e.TradeDate))
        {
            switch (entry.Type)
            {
                case TransactionType.Buy:
                case TransactionType.Reinvest:
                case TransactionType.Transfer:
                    quantity += entry.Quantity ?? 0m;
                    break;
                case TransactionType.Sell:
                    quantity -= entry.Quantity ?? 0m;
                    break;
                case TransactionType.Split:
                    if (splitFactorsByExDate.TryGetValue(entry.TradeDate, out var factor))
                        quantity *= factor;
                    else
                        onUnresolvedSplit?.Invoke(entry.TradeDate);
                    break;
                default:
                    // Dividend, Interest, CapitalGain, Deposit, Withdrawal, Fee, Other — no share effect.
                    break;
            }
        }

        return quantity;
    }
}
