using Shouldly;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

/// <summary>
/// The daily-valued TWR splits the period at every external flow, measures each sub-period from real valuations and
/// chains the results — so the timing of deposits and withdrawals can't move it. Valuations here come from a
/// date → value table standing in for the valuation engine.
/// </summary>
public sealed class DailyValuedTimeWeightedReturnCalculatorTests
{
    private static readonly DateOnly From = new(2025, 1, 1);
    private static readonly DateOnly To = new(2025, 12, 31);

    private readonly DailyValuedTimeWeightedReturnCalculator _calc = new();

    [Fact]
    public void Without_flows_the_return_is_the_growth_from_the_starting_to_the_ending_balance()
    {
        var result = _calc.Compute(Ctx(starting: 1000m, ending: 1100m, flows: [], values: new()));

        result.Rate!.Value.ShouldBe(0.10m, tolerance: 0.000001m);
        result.Method.ShouldBe("DailyValuedTWR");
        result.Basis.ShouldBe("Period");
        result.Reason.ShouldBeNull();
        result.FallbackReason.ShouldBeNull();
    }

    [Fact]
    public void A_deposit_splits_the_period_and_the_sub_period_returns_are_chained()
    {
        // Textbook case: $1000 falls 20% to $800 by the eve of a $1000 deposit on Jul 1; the $1800 then gains 20% to
        // $2160. The investments lost money: TWR = 0.80 × 1.20 − 1 = −4%, whatever the deposit's size or date.
        // Modified Dietz says about +10.6%, because the money added at the bottom did well — that's a money-weighted
        // answer, not how the investments did.
        var deposit = new DateOnly(2025, 7, 1);
        var ctx = Ctx(
            starting: 1000m,
            ending: 2160m,
            flows: [new CashFlow(deposit, 1000m)],
            values: new() { [deposit.AddDays(-1)] = 800m });

        var result = _calc.Compute(ctx);

        result.Rate!.Value.ShouldBe(-0.04m, tolerance: 0.000001m);
        new ModifiedDietzTimeWeightedReturnCalculator().Compute(ctx).Rate!.Value.ShouldBe(0.106m, tolerance: 0.001m);
    }

    [Fact]
    public void A_flow_on_the_first_day_belongs_to_the_first_sub_period()
    {
        // The period opens at the close of the day before `from`, so money arriving on `from` is invested for the
        // whole first sub-period: $0 + $1000 → $1050 is 5%.
        var result = _calc.Compute(Ctx(
            starting: 0m,
            ending: 1050m,
            flows: [new CashFlow(From, 1000m)],
            values: new()));

        result.Rate!.Value.ShouldBe(0.05m, tolerance: 0.000001m);
    }

    [Fact]
    public void Emptying_the_account_and_refunding_it_later_skips_the_empty_stretch()
    {
        // The case that inflates Modified Dietz: $1000 grows to $1100, everything is withdrawn on Apr 1, the account
        // sits empty, and $100 is deposited on Oct 1 and grows to $110. Each invested stretch made 10%, so the TWR is
        // 1.10 × 1.10 − 1 = 21%; the empty months in between neither help nor hurt.
        var withdrawal = new DateOnly(2025, 4, 1);
        var redeposit = new DateOnly(2025, 10, 1);
        var ctx = Ctx(
            starting: 1000m,
            ending: 110m,
            flows: [new CashFlow(withdrawal, -1100m), new CashFlow(redeposit, 100m)],
            values: new() { [withdrawal.AddDays(-1)] = 1100m, [redeposit.AddDays(-1)] = 0m });

        var result = _calc.Compute(ctx);

        result.Rate!.Value.ShouldBe(0.21m, tolerance: 0.000001m);
        result.FallbackReason.ShouldBeNull();
    }

    [Fact]
    public void Flows_that_net_to_zero_on_a_day_do_not_split_the_period()
    {
        // Both legs of an internal transfer land on the same day at portfolio scope. The day needn't be valued:
        // splitting there wouldn't change the chain, and an unpriceable eve would force a needless fallback.
        var transferDay = new DateOnly(2025, 5, 1);
        var ctx = Ctx(
            starting: 1000m,
            ending: 1200m,
            flows: [new CashFlow(transferDay, 500m), new CashFlow(transferDay, -500m)],
            values: new()) with
        {
            ValueAt = _ => throw new InvalidOperationException("No interior valuation should be needed."),
        };

        var result = _calc.Compute(ctx);

        result.Rate!.Value.ShouldBe(0.20m, tolerance: 0.000001m);
    }

    [Fact]
    public void Flows_outside_the_period_are_ignored()
    {
        var result = _calc.Compute(Ctx(
            starting: 1000m,
            ending: 1100m,
            flows: [new CashFlow(From.AddDays(-5), 999m), new CashFlow(To.AddDays(1), 999m)],
            values: new()));

        result.Rate!.Value.ShouldBe(0.10m, tolerance: 0.000001m);
    }

    [Fact]
    public void An_unvaluable_flow_eve_falls_back_to_Modified_Dietz_and_says_why()
    {
        // A holding had no price on the eve of the Jul 1 deposit: the chain can't be measured there, so the figure is
        // the Modified Dietz approximation over the whole period, labelled as such with the cause.
        var deposit = new DateOnly(2025, 7, 1);
        var ctx = Ctx(starting: 1000m, ending: 2310m, flows: [new CashFlow(deposit, 1000m)], values: new()) with
        {
            ValueAt = date => new PerformanceBalanceResult(0m, false, null, 0, 1)
            {
                Missing = [new MissingValuation(Guid.NewGuid(), Guid.NewGuid(), "ZXGAP", "NoPrice")],
            },
        };

        var result = _calc.Compute(ctx);

        var modifiedDietz = new ModifiedDietzTimeWeightedReturnCalculator().Compute(ctx);
        result.Method.ShouldBe("ModifiedDietz");
        result.FallbackReason.ShouldBe("NoPrice");
        result.Rate.ShouldBe(modifiedDietz.Rate);
        result.Reason.ShouldBeNull();
    }

    [Fact]
    public void Without_daily_valuations_a_period_with_flows_falls_back_to_Modified_Dietz()
    {
        var ctx = Ctx(starting: 1000m, ending: 2310m, flows: [new CashFlow(new DateOnly(2025, 7, 1), 1000m)], values: new())
            with { ValueAt = null };

        var result = _calc.Compute(ctx);

        result.Method.ShouldBe("ModifiedDietz");
        result.FallbackReason.ShouldBe("NoDailyValuation");
        result.Rate.ShouldNotBeNull();
    }

    [Fact]
    public void Value_appearing_in_an_empty_account_without_a_deposit_falls_back()
    {
        // Nothing invested at the start and no deposit, yet $500 at the end: the flows are incomplete, and a growth
        // ratio over $0 is meaningless.
        var result = _calc.Compute(Ctx(starting: 0m, ending: 500m, flows: [], values: new()));

        result.Method.ShouldBe("ModifiedDietz");
        result.FallbackReason.ShouldBe("ValueWithoutInvestment");
    }

    [Fact]
    public void Value_vanishing_without_a_withdrawal_falls_back_rather_than_reporting_a_total_loss()
    {
        var result = _calc.Compute(Ctx(starting: 1000m, ending: 0m, flows: [], values: new()));

        result.Method.ShouldBe("ModifiedDietz");
        result.FallbackReason.ShouldBe("ValueVanished");
    }

    [Fact]
    public void An_account_empty_for_the_whole_period_has_no_return()
    {
        var result = _calc.Compute(Ctx(starting: 0m, ending: 0m, flows: [], values: new()));

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe("NoInvestedBalance");
        result.Method.ShouldBe("DailyValuedTWR");
    }

    [Theory]
    [InlineData(false, true, "IncompleteStartingBalance")]
    [InlineData(true, false, "IncompleteEndingBalance")]
    public void An_incomplete_boundary_balance_leaves_the_return_unknown(bool startingComplete, bool endingComplete, string reason)
    {
        var ctx = Ctx(starting: 1000m, ending: 1100m, flows: [], values: new()) with
        {
            StartingIsComplete = startingComplete,
            EndingIsComplete = endingComplete,
        };

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe(reason);
        result.FallbackReason.ShouldBeNull();
    }

    [Fact]
    public void A_single_day_period_is_measured()
    {
        // `from` = `to` spans the close of the day before to the close of the day: one day's return.
        var ctx = Ctx(starting: 1000m, ending: 1010m, flows: [], values: new()) with { To = From };

        _calc.Compute(ctx).Rate!.Value.ShouldBe(0.01m, tolerance: 0.000001m);
    }

    [Fact]
    public void A_period_of_a_year_or_more_also_reports_the_annualized_rate()
    {
        // 21% over two years (730 days) is 10% a year.
        var ctx = Ctx(starting: 1000m, ending: 1210m, flows: [], values: new()) with { To = From.AddDays(730) };

        var result = _calc.Compute(ctx);

        result.Rate!.Value.ShouldBe(0.21m, tolerance: 0.000001m);
        result.PeriodRate!.Value.ShouldBe(0.21m, tolerance: 0.000001m);
        result.AnnualizedRate!.Value.ShouldBe(0.10m, tolerance: 0.000001m);
    }

    [Fact]
    public void A_period_under_a_year_has_no_annualized_rate()
    {
        var ctx = Ctx(starting: 1000m, ending: 1100m, flows: [], values: new()) with { To = From.AddDays(200) };

        _calc.Compute(ctx).AnnualizedRate.ShouldBeNull();
    }

    [Fact]
    public void A_fallback_is_annualized_too()
    {
        var ctx = Ctx(starting: 1000m, ending: 2310m, flows: [new CashFlow(new DateOnly(2025, 7, 1), 1000m)], values: new())
            with { ValueAt = null, To = From.AddDays(730) };

        var result = _calc.Compute(ctx);

        result.FallbackReason.ShouldBe("NoDailyValuation");
        result.AnnualizedRate.ShouldNotBeNull();
    }

    private static PerformanceComputationContext Ctx(
        decimal starting,
        decimal ending,
        IReadOnlyList<CashFlow> flows,
        Dictionary<DateOnly, decimal> values)
        => new(
            From,
            To,
            starting,
            StartingIsComplete: true,
            ending,
            EndingIsComplete: true,
            flows,
            IntermediateBalances: [],
            ValueAt: date => values.TryGetValue(date, out var v)
                ? new PerformanceBalanceResult(v, true, null, 1, 0)
                : throw new InvalidOperationException($"Unexpected valuation date {date}."));
}
