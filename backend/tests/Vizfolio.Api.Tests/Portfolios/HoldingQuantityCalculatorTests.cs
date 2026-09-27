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

    [Theory]
    [InlineData(-5)] // OFX UNITS and the Vanguard report store sold units as negative
    [InlineData(5)]
    public void Sell_reduces_the_position_regardless_of_the_brokers_quantity_sign(decimal soldUnits)
    {
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 10m),
            new LedgerShareEntry(new DateOnly(2025, 4, 1), TransactionType.Sell, soldUnits),
        };

        HoldingQuantityCalculator.QuantityAt(entries, NoSplits, new DateOnly(2025, 12, 31)).ShouldBe(5m);
    }

    [Fact]
    public void Income_rows_carrying_a_quantity_count_as_reinvested_shares()
    {
        // Older Vanguard reports put the reinvested shares on the Dividend / Capital gain row itself;
        // a plain cash distribution carries no quantity and leaves the position unchanged.
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2016, 2, 1), TransactionType.Buy, 100m),
            new LedgerShareEntry(new DateOnly(2016, 3, 31), TransactionType.Dividend, 1.5m),
            new LedgerShareEntry(new DateOnly(2016, 12, 20), TransactionType.CapitalGain, 0.5m),
            new LedgerShareEntry(new DateOnly(2019, 3, 29), TransactionType.Dividend, null),
        };

        HoldingQuantityCalculator.QuantityAt(entries, NoSplits, new DateOnly(2025, 12, 31)).ShouldBe(102m);
    }

    [Fact]
    public void Rolling_forward_from_an_anchor_ignores_rows_on_or_before_it()
    {
        var entries = new[]
        {
            new LedgerShareEntry(new DateOnly(2025, 1, 1), TransactionType.Buy, 999m), // already in the anchor
            new LedgerShareEntry(new DateOnly(2025, 3, 1), TransactionType.Buy, 10m),
        };

        HoldingQuantityCalculator.RollForward(100m, new DateOnly(2025, 1, 1), entries, NoSplits, new DateOnly(2025, 12, 31))
            .ShouldBe(110m);
    }

    [Fact]
    public void Cash_only_income_after_a_date_is_not_share_activity()
    {
        var entries = new[] { new LedgerShareEntry(new DateOnly(2025, 3, 1), TransactionType.Dividend, null) };

        HoldingQuantityCalculator.HasActivityBetween(entries, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))
            .ShouldBeFalse();
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
            new LedgerShareEntry(new DateOnly(2025, 2, 1), TransactionType.Dividend, null), // cash dividend
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
