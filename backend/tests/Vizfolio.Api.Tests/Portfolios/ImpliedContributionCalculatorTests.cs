using Shouldly;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class ImpliedContributionCalculatorTests
{
    private static readonly DateOnly Day1 = new(2012, 3, 1);
    private static readonly DateOnly Day2 = new(2012, 6, 1);
    private static readonly DateOnly Day3 = new(2012, 9, 4);

    private static CashLedgerRow Row(
        DateOnly date, TransactionType type, decimal amount,
        decimal? quantity = null, string? ticker = null, string? sourceType = null, DateOnly? settles = null)
        => new(date, type, amount, quantity, ticker, sourceType, settles);

    [Fact]
    public void Fund_company_purchase_with_no_deposit_implies_a_contribution_of_its_cost()
    {
        // Older fund-company reports record the purchase as a *positive* amount and show no deposit:
        // the contribution went straight into the fund.
        var rows = new[] { Row(Day1, TransactionType.Buy, 1000m, quantity: 100m, ticker: "FUNDX") };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Day1);
        implied.Amount.ShouldBe(1000m);
        implied.CashBefore.ShouldBe(0m);
    }

    [Fact]
    public void Purchase_funded_by_a_recorded_deposit_implies_nothing()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 1000m),
            Row(Day1, TransactionType.Buy, -1000m, quantity: 100m, ticker: "FUNDX"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
    }

    [Fact]
    public void Purchases_paid_from_sale_proceeds_and_cash_dividends_imply_nothing()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 1000m),
            Row(Day1, TransactionType.Buy, -1000m, quantity: 100m, ticker: "AAA"),
            Row(Day2, TransactionType.Dividend, 25m, ticker: "AAA"),         // cash dividend
            Row(Day2, TransactionType.Reinvest, -25m, quantity: 2.5m, ticker: "AAA"),
            Row(Day3, TransactionType.Sell, 600m, quantity: -50m, ticker: "AAA"),
            Row(Day3, TransactionType.Buy, -600m, quantity: 30m, ticker: "BBB"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void Income_reinvested_at_source_carries_no_cash()
    {
        // Older reports put the reinvested shares on the dividend row itself, with no Reinvest row.
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 1000m),
            Row(Day1, TransactionType.Buy, -1000m, quantity: 100m, ticker: "FUNDX"),
            Row(Day2, TransactionType.Dividend, 12m, quantity: 1.2m, ticker: "FUNDX"),
            Row(Day2, TransactionType.CapitalGain, 3m, quantity: 0.3m, ticker: "FUNDX"),
        };

        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void Settlement_fund_sweeps_and_a_cash_conversion_out_net_to_zero()
    {
        // Brokerage-era IRA → Roth pattern: two contributions swept into the settlement fund, its
        // dividend reinvested, then swept back out and converted in cash the same day as a final dividend.
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 1000m, sourceType: "Contribution"),
            Row(Day1, TransactionType.Deposit, 1000m, sourceType: "Contribution"),
            Row(Day1, TransactionType.Other, -2000m, ticker: "SETTLE", sourceType: "Sweep in"),
            Row(Day2, TransactionType.Dividend, 0.40m, ticker: "SETTLE"),
            Row(Day2, TransactionType.Reinvest, -0.40m, ticker: "SETTLE"),
            Row(Day3, TransactionType.Transfer, -2000.70m, sourceType: "Conversion (outgoing)"),
            Row(Day3, TransactionType.Other, 2000.40m, ticker: "SETTLE", sourceType: "Sweep out"),
            Row(Day3, TransactionType.Dividend, 0.30m, ticker: "SETTLE"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void In_kind_transfers_and_share_class_conversions_move_no_cash()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Transfer, 1000m, quantity: 100m, ticker: "FUNDX", sourceType: "TRANSFER FROM 111"),
            Row(Day2, TransactionType.Sell, -1100m, quantity: -100m, ticker: "FUNDX", sourceType: "Share Conversion (outgoing)"),
            Row(Day2, TransactionType.Buy, 1100m, quantity: 100m, ticker: "FUNDY", sourceType: "Share Conversion (incoming)"),
            Row(Day3, TransactionType.Transfer, -1150m, quantity: -100m, ticker: "FUNDY", sourceType: "Conversion (outgoing)"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void Row_order_within_a_day_does_not_matter()
    {
        // The purchase is listed before the deposit that funded it.
        var rows = new[]
        {
            Row(Day1, TransactionType.Buy, -500m, quantity: 5m, ticker: "AAA"),
            Row(Day1, TransactionType.Deposit, 500m),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.50, 0)]   // rounding cents: ignored
    [InlineData(5.00, 1)]   // a real shortfall
    public void Shortfalls_within_the_tolerance_are_ignored(decimal shortfall, int expectedCount)
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 100m),
            Row(Day1, TransactionType.Buy, -(100m + shortfall), quantity: 1m, ticker: "AAA"),
        };

        ImpliedContributionCalculator.Find(rows).Count.ShouldBe(expectedCount);
    }

    [Fact]
    public void Cash_resets_after_each_implied_contribution_so_every_unfunded_purchase_is_counted()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Buy, 1000m, quantity: 100m, ticker: "FUNDX"),
            Row(Day2, TransactionType.Buy, 500m, quantity: 45m, ticker: "FUNDX"),
            Row(Day3, TransactionType.Deposit, 300m),
        };

        var implied = ImpliedContributionCalculator.Find(rows);

        implied.Select(c => (c.Date, c.Amount)).ShouldBe(new[] { (Day1, 1000m), (Day2, 500m) });
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(300m);
    }

    [Fact]
    public void A_day_that_funds_part_of_a_purchase_implies_only_the_shortfall()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 400m),
            Row(Day2, TransactionType.Buy, -1000m, quantity: 10m, ticker: "AAA"),
        };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Amount.ShouldBe(600m);
        implied.CashBefore.ShouldBe(400m);
        implied.DayRows.ShouldHaveSingleItem().Row.Type.ShouldBe(TransactionType.Buy);
    }

    [Fact]
    public void A_sweep_recorded_by_two_sources_as_a_buy_and_a_sweep_counts_once()
    {
        // One source records cash moving into the settlement fund as a Buy of the fund, the other as a
        // "Sweep in" — the same event. The settlement fund is cash, so neither spends the deposit.
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 700m),
            Row(Day1, TransactionType.Buy, -700m, quantity: 700m, ticker: "SETTLE"),
            Row(Day1, TransactionType.Other, -700m, ticker: "SETTLE", sourceType: "Sweep in"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(700m); // still available, held in the fund
    }

    [Fact]
    public void Buying_a_fund_from_the_settlement_fund_spends_cash_and_its_income_adds_to_it()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 1000m),
            Row(Day1, TransactionType.Other, -1000m, ticker: "SETTLE", sourceType: "Sweep in"),
            Row(Day2, TransactionType.Dividend, 2m, ticker: "SETTLE"),
            Row(Day2, TransactionType.Reinvest, -2m, quantity: 2m, ticker: "SETTLE"),
            Row(Day3, TransactionType.Sell, 1002m, quantity: -1002m, ticker: "SETTLE"),
            Row(Day3, TransactionType.Other, 1002m, ticker: "SETTLE", sourceType: "Sweep out"),
            Row(Day3, TransactionType.Buy, -1002m, quantity: 10m, ticker: "AAA"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void Settlement_fund_is_recognised_from_sweep_rows_only()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Other, -5m, ticker: "settle", sourceType: "Sweep in"),
            Row(Day1, TransactionType.Buy, -5m, quantity: 1m, ticker: "AAA"),
        };

        ImpliedContributionCalculator.SettlementTickers(rows).ShouldBe(new[] { "SETTLE" });
    }

    [Fact]
    public void A_purchase_funded_by_cash_arriving_before_it_settles_implies_nothing()
    {
        // An ETF bought on Day1 settles three days later; the money funding it lands the day after the
        // trade. By trade date the account looks short; by settlement date it never is.
        var trade = new DateOnly(2012, 4, 13);
        var rows = new[]
        {
            Row(trade, TransactionType.Buy, -1000m, quantity: 10m, ticker: "AAA", settles: trade.AddDays(3)),
            Row(trade.AddDays(1), TransactionType.Deposit, 1000m),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void An_unfunded_purchase_is_implied_on_its_settlement_date()
    {
        var trade = new DateOnly(2012, 4, 13);
        var rows = new[] { Row(trade, TransactionType.Buy, -1000m, quantity: 10m, ticker: "AAA", settles: trade.AddDays(3)) };

        ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem().Date.ShouldBe(trade.AddDays(3));
    }

    [Fact]
    public void A_reinvestment_with_no_income_row_is_funded_by_the_income_it_reinvests()
    {
        // A QFX REINVEST folds the dividend into the reinvestment row; there's no separate income row.
        var rows = new[] { Row(Day1, TransactionType.Reinvest, 340m, quantity: 2.6m, ticker: "FUNDX") };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void A_dividend_and_its_reinvestment_on_separate_rows_still_net_to_zero()
    {
        // The Vanguard report's two-row form — including a dividend row with no ticker.
        var rows = new[]
        {
            Row(Day1, TransactionType.Dividend, 12m),
            Row(Day1, TransactionType.Reinvest, -12m, quantity: 1.2m, ticker: "FUNDX"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void Reinvestments_never_mask_an_unfunded_purchase_the_same_day()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Reinvest, 50m, quantity: 0.5m, ticker: "FUNDX"),
            Row(Day1, TransactionType.Buy, -1000m, quantity: 10m, ticker: "AAA"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem().Amount.ShouldBe(1000m);
    }
}
