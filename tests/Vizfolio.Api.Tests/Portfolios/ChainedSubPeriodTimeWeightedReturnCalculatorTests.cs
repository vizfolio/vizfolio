using Shouldly;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class ChainedSubPeriodTimeWeightedReturnCalculatorTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2026, 1, 1);

    private readonly ChainedSubPeriodTimeWeightedReturnCalculator _calc = new();

    [Fact]
    public void Reports_ChainedSubPeriods_method_and_Period_basis()
    {
        var ctx = Ctx(
            interim: new[] { new BalancePoint(Start.AddDays(180), 1100m) });

        var result = _calc.Compute(ctx);

        result.Method.ShouldBe("ChainedSubPeriods");
        result.Basis.ShouldBe("Period");
    }

    [Fact]
    public void Returns_null_with_reason_when_starting_balance_incomplete()
    {
        var ctx = Ctx(startingComplete: false);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe("IncompleteStartingBalance");
    }

    [Fact]
    public void Returns_null_when_no_interior_snapshots_exist()
    {
        var ctx = Ctx(interim: Array.Empty<BalancePoint>());

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe("InsufficientIntermediateSnapshots");
    }

    [Fact]
    public void Chains_two_sub_period_returns_with_no_cash_flows()
    {
        // BMV=1000 → interior 1100 (day 180) → EMV=1300 (day 365)
        // sub1 = 1100/1000 - 1 = 0.10
        // sub2 = 1300/1100 - 1 = 0.1818...
        // total = 1.10 * (1300/1100) - 1 = 1.30 - 1 = 0.30
        var ctx = Ctx(
            starting: 1000m,
            ending: 1300m,
            interim: new[] { new BalancePoint(Start.AddDays(180), 1100m) });

        var result = _calc.Compute(ctx);

        result.Rate.ShouldNotBeNull();
        Math.Abs(result.Rate!.Value - 0.30m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void With_single_interior_and_no_flows_matches_simple_growth()
    {
        // A single interior point that agrees with equal growth on both sides.
        var ctx = Ctx(
            starting: 1000m,
            ending: 1210m,
            interim: new[] { new BalancePoint(Start.AddDays(182), 1100m) });

        var result = _calc.Compute(ctx);

        // sub1 = 1.10, sub2 = 1210/1100 = 1.10, total = 1.21 - 1 = 0.21
        Math.Abs(result.Rate!.Value - 0.21m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void Cash_flow_inside_a_sub_period_uses_modified_dietz_weighting()
    {
        // Sub-period from day 0 → day 180: BMV=1000, endValue=1200, +100 at day 90.
        // Net flow = 100, weighted = (180-90)/180 * 100 = 50, denom = 1050
        // sub1 return = (1200 - 1000 - 100) / 1050 = 100/1050
        // Sub-period from day 180 → day 365: no flows, 1200 → 1300, return = 100/1200
        // Cumulative = (1 + 100/1050) * (1 + 100/1200) - 1
        var ctx = Ctx(
            starting: 1000m,
            ending: 1300m,
            interim: new[] { new BalancePoint(Start.AddDays(180), 1200m) },
            flows: new[] { new CashFlow(Start.AddDays(90), 100m) });

        var result = _calc.Compute(ctx);

        var sub1 = 100m / 1050m;
        var sub2 = 100m / 1200m;
        var expected = (1m + sub1) * (1m + sub2) - 1m;
        Math.Abs(result.Rate!.Value - expected).ShouldBeLessThan(0.0001m);
    }

    private static PerformanceComputationContext Ctx(
        decimal starting = 1000m,
        decimal ending = 1000m,
        IReadOnlyList<BalancePoint>? interim = null,
        IReadOnlyList<CashFlow>? flows = null,
        bool startingComplete = true,
        bool endingComplete = true) =>
        new(
            From: Start,
            To: End,
            StartingBalance: starting,
            StartingIsComplete: startingComplete,
            EndingBalance: ending,
            EndingIsComplete: endingComplete,
            CashFlows: flows ?? Array.Empty<CashFlow>(),
            IntermediateBalances: interim ?? Array.Empty<BalancePoint>());
}
