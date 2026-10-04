using Shouldly;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class XirrMoneyWeightedReturnCalculatorTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2026, 1, 1); // exactly 365 days later

    private readonly XirrMoneyWeightedReturnCalculator _calc = new();

    [Fact]
    public void Reports_XIRR_method_and_Annualized_basis()
    {
        var ctx = Ctx(starting: 1000m, ending: 1100m);

        var result = _calc.Compute(ctx);

        result.Method.ShouldBe("XIRR");
        result.Basis.ShouldBe("Annualized");
    }

    [Fact]
    public void Returns_null_when_starting_balance_incomplete()
    {
        var ctx = Ctx(starting: 1000m, ending: 1100m, startingComplete: false);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe("IncompleteStartingBalance");
    }

    [Fact]
    public void Simple_365_day_period_produces_annualized_rate_of_ten_percent()
    {
        // -1000 at day 0, +1100 at day 365 → r = 10%
        var ctx = Ctx(starting: 1000m, ending: 1100m);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldNotBeNull();
        Math.Abs(result.Rate!.Value - 0.10m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void A_period_shorter_than_a_year_reports_the_period_return_not_an_annualized_one()
    {
        // 1000 → 1100 over ~182 days. Annualizing would report ≈ 21%; for a part-year period the return the
        // investor actually got over the period (10%) is reported instead, labelled "Period".
        var half = Start.AddDays(182);
        var ctx = Ctx(starting: 1000m, ending: 1100m, from: Start, to: half);

        var result = _calc.Compute(ctx);

        result.Basis.ShouldBe("Period");
        result.Rate!.Value.ShouldBe(0.10m, tolerance: 0.0001m);
    }

    [Fact]
    public void A_large_move_over_a_few_days_is_reported_without_overflowing_the_solver()
    {
        // +30% in a week annualizes to ~84,000,000% — beyond the solver's bounds. As a period return it's 30%.
        var week = Start.AddDays(7);
        var ctx = Ctx(starting: 1000m, ending: 1300m, from: Start, to: week);

        var result = _calc.Compute(ctx);

        result.Basis.ShouldBe("Period");
        result.Rate!.Value.ShouldBe(0.30m, tolerance: 0.0001m);
    }

    [Fact]
    public void A_part_year_deposit_is_weighted_by_how_long_it_was_invested()
    {
        // 1000 invested for 100 days, then +1000 for the last 50: the 150-day period return lies between
        // the simple gain on all money (300 / 2000 = 15%) and on the first deposit alone (30%), and NPV ≈ 0.
        var end = Start.AddDays(150);
        var ctx = Ctx(
            starting: 1000m,
            ending: 2300m,
            flows: new[] { new CashFlow(Start.AddDays(100), 1000m) },
            from: Start,
            to: end);

        var result = _calc.Compute(ctx);

        result.Basis.ShouldBe("Period");
        var r = (double)result.Rate!.Value;
        r.ShouldBeGreaterThan(0.15);
        r.ShouldBeLessThan(0.30);
        var npv = -1000.0 - 1000.0 / Math.Pow(1 + r, 100.0 / 150.0) + 2300.0 / (1 + r);
        Math.Abs(npv).ShouldBeLessThan(0.01);
    }

    [Fact]
    public void Mid_period_deposit_is_reflected_in_the_rate()
    {
        // BMV=1000 at day 0, +500 deposit at day 100, EMV=1600 at day 365.
        // Solve for r: -1000 + -500/(1+r)^(100/365) + 1600/(1+r) = 0
        var ctx = Ctx(
            starting: 1000m,
            ending: 1600m,
            flows: new[] { new CashFlow(Start.AddDays(100), 500m) });

        var result = _calc.Compute(ctx);

        result.Rate.ShouldNotBeNull();
        result.Rate!.Value.ShouldBeGreaterThan(0.05m);
        result.Rate.Value.ShouldBeLessThan(0.15m);
        // Verify NPV ≈ 0 at the computed rate
        var r = (double)result.Rate.Value;
        var npv = -1000.0
                  + -500.0 / Math.Pow(1 + r, 100.0 / 365.0)
                  + 1600.0 / Math.Pow(1 + r, 365.0 / 365.0);
        Math.Abs(npv).ShouldBeLessThan(0.01);
    }

    [Fact]
    public void Withdrawal_mid_period_is_reflected_in_the_rate()
    {
        // BMV=1000 at day 0, -200 withdrawal at day 200, EMV=900 at day 365.
        // Investor gained: 200 (out) + 900 (end) = 1100 vs 1000 in → ~10% gross.
        var ctx = Ctx(
            starting: 1000m,
            ending: 900m,
            flows: new[] { new CashFlow(Start.AddDays(200), -200m) });

        var result = _calc.Compute(ctx);

        result.Rate.ShouldNotBeNull();
        result.Rate!.Value.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void Negative_return_produces_negative_rate()
    {
        // BMV=1000, EMV=800, one year → -20% annualized.
        var ctx = Ctx(starting: 1000m, ending: 800m);

        var result = _calc.Compute(ctx);

        Math.Abs(result.Rate!.Value - -0.20m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void Returns_null_when_there_is_no_sign_change_in_cash_flow_series()
    {
        // All outflows (should never happen in practice: we always inject EMV as positive
        // unless it is zero, but if EMV also happens to net to zero after aggregation
        // there is no way to solve for r).
        var ctx = Ctx(starting: 1000m, ending: 0m);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        // NoSignChange or InsufficientCashFlows both acceptable — both mean unsolvable.
        (result.Reason == "NoSignChange" || result.Reason == "InsufficientCashFlows").ShouldBeTrue();
    }

    [Fact]
    public void A_period_of_a_year_or_more_gives_the_rate_per_year_and_the_total_it_compounds_to()
    {
        // 1000 → 1210 over two years is 10% a year, which is 21% in total.
        var ctx = Ctx(starting: 1000m, ending: 1210m, to: Start.AddDays(730));

        var result = _calc.Compute(ctx);

        result.Basis.ShouldBe("Annualized");
        result.Rate!.Value.ShouldBe(0.10m, tolerance: 0.0001m);
        result.AnnualizedRate!.Value.ShouldBe(result.Rate.Value);
        result.PeriodRate!.Value.ShouldBe(0.21m, tolerance: 0.0001m);
    }

    [Fact]
    public void The_total_compounds_only_over_the_time_the_money_was_invested()
    {
        // An account opened half-way through a two-year period: nothing at the start, 1000 deposited on day 365,
        // 1100 at the end. The money grew 10% in total (in its one year), not 10% a year for two years.
        var ctx = Ctx(starting: 0m, ending: 1100m, to: Start.AddDays(730),
            flows: [new CashFlow(Start.AddDays(365), 1000m)]);

        var result = _calc.Compute(ctx);

        result.AnnualizedRate!.Value.ShouldBe(0.10m, tolerance: 0.0001m);
        result.PeriodRate!.Value.ShouldBe(0.10m, tolerance: 0.0001m);
    }

    [Fact]
    public void A_period_under_a_year_gives_only_the_total()
    {
        var ctx = Ctx(starting: 1000m, ending: 1100m, to: Start.AddDays(182));

        var result = _calc.Compute(ctx);

        result.PeriodRate!.Value.ShouldBe(0.10m, tolerance: 0.0001m);
        result.AnnualizedRate.ShouldBeNull();
    }

    [Fact]
    public void Without_a_rate_neither_form_is_given()
    {
        var result = _calc.Compute(Ctx(starting: 1000m, ending: 1100m, startingComplete: false));

        result.PeriodRate.ShouldBeNull();
        result.AnnualizedRate.ShouldBeNull();
    }

    private static PerformanceComputationContext Ctx(
        decimal starting,
        decimal ending,
        IReadOnlyList<CashFlow>? flows = null,
        DateOnly? from = null,
        DateOnly? to = null,
        bool startingComplete = true,
        bool endingComplete = true) =>
        new(
            From: from ?? Start,
            To: to ?? End,
            StartingBalance: starting,
            StartingIsComplete: startingComplete,
            EndingBalance: ending,
            EndingIsComplete: endingComplete,
            CashFlows: flows ?? Array.Empty<CashFlow>(),
            IntermediateBalances: Array.Empty<BalancePoint>());
}
