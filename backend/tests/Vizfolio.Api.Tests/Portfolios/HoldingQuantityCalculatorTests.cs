using Shouldly;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class HoldingQuantityCalculatorTests
{
    private static readonly IReadOnlyDictionary<DateOnly, decimal> NoSplits =
        new Dictionary<DateOnly, decimal>();

    [Fact]
    public void Rolls_buys_reinvests_and_transfers_up_and_sells_down()
    {
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 10m),
            new LedgerShareEntry(new DateOnly(2025, 2, 1), TransactionType.Reinvest, 1m),
            new LedgerShareEntry(new DateOnly(2025, 3, 1), TransactionType.Transfer, 4m),
            new LedgerShareEntry(new DateOnly(2025, 4, 1), TransactionType.Sell, 5m),
        };

        HoldingQuantityCalculator.QuantityAt(entries, NoSplits, new DateOnly(2025, 12, 31))
            .ShouldBe(10m); // 10 + 1 + 4 - 5
    }

    [Fact]
    public void Only_counts_entries_on_or_before_the_date()
    {
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 10m),
            new LedgerShareEntry(new DateOnly(2025, 6, 1), TransactionType.Buy, 5m),
        };

        HoldingQuantityCalculator.QuantityAt(entries, NoSplits, new DateOnly(2025, 3, 1)).ShouldBe(10m);
    }

    [Fact]
    public void Ignores_non_share_types()
    {
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 10m),
            new LedgerShareEntry(new DateOnly(2025, 2, 1), TransactionType.Dividend, 100m),
            new LedgerShareEntry(new DateOnly(2025, 3, 1), TransactionType.Deposit, 500m),
            new LedgerShareEntry(new DateOnly(2025, 4, 1), TransactionType.Fee, 5m),
        };

        HoldingQuantityCalculator.QuantityAt(entries, NoSplits, new DateOnly(2025, 12, 31)).ShouldBe(10m);
    }

    [Fact]
    public void Split_with_matching_factor_multiplies_quantity_held_before_ex_date()
    {
        var exDate = new DateOnly(2025, 6, 1);
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 10m),
            new LedgerShareEntry(exDate, TransactionType.Split, null),
            new LedgerShareEntry(new DateOnly(2025, 9, 1), TransactionType.Buy, 2m),
        };
        var splits = new Dictionary<DateOnly, decimal> { [exDate] = 4m }; // 4-for-1

        // Before split: 10. After split: 40. Plus a later 2-share buy → 42.
        HoldingQuantityCalculator.QuantityAt(entries, splits, new DateOnly(2025, 5, 1)).ShouldBe(10m);
        HoldingQuantityCalculator.QuantityAt(entries, splits, new DateOnly(2025, 6, 1)).ShouldBe(40m);
        HoldingQuantityCalculator.QuantityAt(entries, splits, new DateOnly(2025, 12, 31)).ShouldBe(42m);
    }

    [Fact]
    public void Split_without_a_resolvable_factor_is_a_no_op_and_reports_it()
    {
        var exDate = new DateOnly(2025, 6, 1);
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 10m),
            new LedgerShareEntry(exDate, TransactionType.Split, null),
        };

        var unresolved = new List<DateOnly>();
        var quantity = HoldingQuantityCalculator.QuantityAt(
            entries, NoSplits, new DateOnly(2025, 12, 31), unresolved.Add);

        quantity.ShouldBe(10m); // unchanged — never silently corrupts the position
        unresolved.ShouldHaveSingleItem().ShouldBe(exDate);
    }
}
