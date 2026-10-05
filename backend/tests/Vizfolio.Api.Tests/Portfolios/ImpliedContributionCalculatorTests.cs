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
    public void Return_of_capital_and_cash_in_lieu_of_fractional_shares_are_cash_the_account_can_spend()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.ReturnOfCapital, 60m, ticker: "FUNDX"),
            Row(Day1, TransactionType.Split, 40m, quantity: 10m, ticker: "FUNDX"),
            Row(Day2, TransactionType.Buy, -100m, quantity: 1m, ticker: "OTHER"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
    }

    [Fact]
    public void A_journal_between_sub_accounts_moves_no_cash()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Journal, 500m),
            Row(Day2, TransactionType.Buy, -500m, quantity: 5m, ticker: "FUNDX"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem().Amount.ShouldBe(500m);
    }

    [Fact]
    public void A_row_the_broker_marks_as_a_settlement_fund_movement_makes_its_ticker_cash_without_a_sweep_label()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 300m),
            new CashLedgerRow(Day1, TransactionType.Buy, -300m, 300m, "CORE", null, IsSettlementFund: true),
            Row(Day2, TransactionType.Buy, -300m, quantity: 3m, ticker: "FUNDX"),
        };

        ImpliedContributionCalculator.SettlementTickers(rows).ShouldBe(["CORE"]);
        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
    }

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

    [Fact]
    public void Money_bought_straight_into_the_money_market_fund_is_an_implied_contribution()
    {
        // An old fund-company account: each contribution is a positive-amount purchase of the money-market
        // (settlement) fund, with no deposit row. Later a sweep moves the money out to buy an ETF. The money came
        // from outside, so each purchase is implied — not invisible because the fund doubles as cash.
        var rows = new[]
        {
            Row(Day1, TransactionType.Buy, 200m, quantity: 200m, ticker: "SETTLE"),
            Row(Day2, TransactionType.Buy, 200m, quantity: 200m, ticker: "SETTLE"),
            Row(Day3, TransactionType.Other, 400m, sourceType: "Sweep out"),
            Row(Day3, TransactionType.Other, 400m, quantity: -400m, ticker: "SETTLE", sourceType: "Sweep"),
            Row(Day3, TransactionType.Buy, -400m, quantity: 4m, ticker: "ETF"),
        };

        var implied = ImpliedContributionCalculator.Find(rows);

        implied.Select(c => (c.Date, c.Amount)).ShouldBe(new[] { (Day1, 200m), (Day2, 200m) });
    }

    [Fact]
    public void A_sweep_into_the_money_market_fund_reported_as_a_purchase_stays_neutral()
    {
        // A QFX reports cash moving into the settlement fund as a Buy with a negative amount: it's cash ↔ cash.
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 500m),
            Row(Day2, TransactionType.Buy, -500m, quantity: 500m, ticker: "SETTLE"),
            Row(Day2, TransactionType.Other, -500m, ticker: "SETTLE", sourceType: "Sweep in"),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
    }

    // ---------------- settlement window ----------------

    private static readonly DateOnly Holiday = new(2012, 10, 8); // banks closed, markets open

    [Fact]
    public void Buying_with_a_deposit_still_clearing_is_not_outside_money()
    {
        // Instant buying power: the purchases go through on a bank holiday, and the deposit that paid for them is
        // recorded the next day, when the ACH completes.
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -280m, quantity: 1m, ticker: "AAA"),
            Row(Holiday, TransactionType.Buy, -469.95m, quantity: 2m, ticker: "BBB"),
            Row(Holiday.AddDays(1), TransactionType.Deposit, 750m),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0.05m);
    }

    [Fact]
    public void A_shortfall_can_be_repaid_by_any_incoming_cash_not_just_one_matching_deposit()
    {
        // 850 spent, then repaid over the next days by a dividend and a smaller deposit together.
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -850m, quantity: 6m, ticker: "AAA"),
            Row(Holiday.AddDays(1), TransactionType.Dividend, 100m, ticker: "BBB"),
            Row(Holiday.AddDays(3), TransactionType.Deposit, 750m),
        };

        ImpliedContributionCalculator.Find(rows).ShouldBeEmpty();
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void A_shortfall_still_unpaid_after_the_window_is_implied_on_the_day_it_was_spent()
    {
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -1000m, quantity: 10m, ticker: "AAA"),
            Row(Holiday.AddDays(8), TransactionType.Deposit, 1000m),
        };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Holiday);
        implied.Amount.ShouldBe(1000m);
        implied.DayRows.ShouldHaveSingleItem().Row.Type.ShouldBe(TransactionType.Buy);
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(1000m); // the later deposit is cash on top
    }

    [Fact]
    public void Only_the_part_of_a_shortfall_left_unpaid_by_its_window_is_implied()
    {
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -1000m, quantity: 10m, ticker: "AAA"),
            Row(Holiday.AddDays(2), TransactionType.Deposit, 300m),
            Row(Holiday.AddDays(30), TransactionType.Deposit, 50m),
        };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Holiday);
        implied.Amount.ShouldBe(700m);
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(50m);
    }

    [Fact]
    public void Incoming_cash_repays_the_oldest_shortfall_first()
    {
        // Two days short; a deposit inside the first one's window repays it, leaving only the second.
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -400m, quantity: 4m, ticker: "AAA"),
            Row(Holiday.AddDays(2), TransactionType.Buy, -250m, quantity: 2m, ticker: "BBB"),
            Row(Holiday.AddDays(4), TransactionType.Deposit, 400m),
            Row(Holiday.AddDays(40), TransactionType.Deposit, 10m),
        };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Holiday.AddDays(2));
        implied.Amount.ShouldBe(250m);
    }

    [Fact]
    public void A_shortfall_wrongly_taken_as_in_flight_resurfaces_when_cash_next_runs_short()
    {
        // A small unfunded purchase, then an unrelated deposit inside its window. The deposit is taken as repaying
        // it, so when the deposit is spent in full later, the gap appears then: the money is implied late, not lost.
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -50m, quantity: 1m, ticker: "AAA"),
            Row(Holiday.AddDays(5), TransactionType.Deposit, 5000m),
            Row(Holiday.AddDays(60), TransactionType.Buy, -5000m, quantity: 50m, ticker: "BBB"),
        };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Holiday.AddDays(60));
        implied.Amount.ShouldBe(50m);
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void A_shortfall_still_open_when_the_history_ends_is_implied()
    {
        // The deposit hasn't cleared by the export date; a later import that brings it retires the implied row.
        var rows = new[] { Row(Holiday, TransactionType.Buy, -750m, quantity: 5m, ticker: "AAA") };

        ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem().Amount.ShouldBe(750m);
        ImpliedContributionCalculator.Find(rows.Append(Row(Holiday.AddDays(1), TransactionType.Deposit, 750m)))
            .ShouldBeEmpty();
    }

    [Fact]
    public void A_window_of_zero_is_the_strict_daily_roll()
    {
        var rows = new[]
        {
            Row(Holiday, TransactionType.Buy, -750m, quantity: 5m, ticker: "AAA"),
            Row(Holiday.AddDays(1), TransactionType.Deposit, 750m),
        };

        var implied = ImpliedContributionCalculator.Find(rows, settlementDays: 0).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Holiday);
        implied.Amount.ShouldBe(750m);
        ImpliedContributionCalculator.EndingCash(rows, settlementDays: 0).ShouldBe(750m);
    }

    [Fact]
    public void A_residue_within_the_tolerance_folds_into_the_next_implied_shortfall()
    {
        var rows = new[]
        {
            Row(Day1, TransactionType.Deposit, 100m),
            Row(Day1, TransactionType.Buy, -100.50m, quantity: 1m, ticker: "AAA"),
            Row(Day2, TransactionType.Buy, -200m, quantity: 2m, ticker: "AAA"),
        };

        var implied = ImpliedContributionCalculator.Find(rows).ShouldHaveSingleItem();

        implied.Date.ShouldBe(Day2);
        implied.Amount.ShouldBe(200.50m);
        ImpliedContributionCalculator.EndingCash(rows).ShouldBe(0m);
    }

    [Fact]
    public void Opening_cash_is_never_a_debt()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => ImpliedContributionCalculator.Find([Row(Day1, TransactionType.Deposit, 1m)], openingCash: -1m));
}
