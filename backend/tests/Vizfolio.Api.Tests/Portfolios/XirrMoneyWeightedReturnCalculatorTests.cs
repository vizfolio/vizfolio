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
    public void Half_year_ten_percent_gain_annualizes_to_roughly_twenty_one_percent()
    {
        // 1000 → 1100 over ~182 days (half year). Annualized ≈ 1.10^2 - 1 = 21%.
        var half = Start.AddDays(182);
        var ctx = Ctx(starting: 1000m, ending: 1100m, from: Start, to: half);

        var result = _calc.Compute(ctx);

        var expected = (decimal)(Math.Pow(1.10, 365.0 / 182.0) - 1);
        Math.Abs(result.Rate!.Value - expected).ShouldBeLessThan(0.001m);
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
