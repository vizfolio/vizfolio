using Shouldly;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class ModifiedDietzTimeWeightedReturnCalculatorTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2026, 1, 1); // 365 days

    private readonly ModifiedDietzTimeWeightedReturnCalculator _calc = new();

    [Fact]
    public void Returns_null_with_reason_when_starting_balance_incomplete()
    {
        var ctx = Ctx(startingComplete: false, endingComplete: true);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe("IncompleteStartingBalance");
        result.Method.ShouldBe("ModifiedDietz");
        result.Basis.ShouldBe("Period");
    }

    [Fact]
    public void Returns_null_with_reason_when_ending_balance_incomplete()
    {
        var ctx = Ctx(startingComplete: true, endingComplete: false);

        var result = _calc.Compute(ctx);

        result.Reason.ShouldBe("IncompleteEndingBalance");
    }

    [Fact]
    public void Returns_null_with_reason_when_from_equals_to()
    {
        var ctx = Ctx(from: End, to: End);

        var result = _calc.Compute(ctx);

        result.Reason.ShouldBe("PeriodTooShort");
    }

    [Fact]
    public void Simple_period_with_no_flows_returns_gain_over_starting_balance()
    {
        var ctx = Ctx(starting: 1000m, ending: 1100m);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldNotBeNull();
        Math.Abs(result.Rate!.Value - 0.10m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void Mid_period_deposit_is_weighted_by_time_remaining()
    {
        // BMV=1000, EMV=1200, +100 deposit at day ~182 (half period), 365 days.
        var midpoint = Start.AddDays(182);
        var ctx = Ctx(
            starting: 1000m,
            ending: 1200m,
            flows: new[] { new CashFlow(midpoint, 100m) });

        var result = _calc.Compute(ctx);

        // R = (1200 - 1000 - 100) / (1000 + 0.5013... * 100) ≈ 100 / 1050.13
        result.Rate.ShouldNotBeNull();
        Math.Abs(result.Rate!.Value - (100m / (1000m + 100m * (365m - 182m) / 365m))).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void Deposit_at_start_of_period_carries_full_weight()
    {
        var ctx = Ctx(
            starting: 0m,
            ending: 1100m,
            flows: new[] { new CashFlow(Start, 1000m) });

        var result = _calc.Compute(ctx);

        // R = (1100 - 0 - 1000) / (0 + 1.0 * 1000) = 100 / 1000 = 0.10
        Math.Abs(result.Rate!.Value - 0.10m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void Deposit_at_end_of_period_carries_zero_weight()
    {
        // A deposit at To brings cash in that couldn't have grown yet — Modified Dietz assigns weight 0.
        var ctx = Ctx(
            starting: 1000m,
            ending: 1100m,
            flows: new[] { new CashFlow(End, 100m) });

        var result = _calc.Compute(ctx);

        // R = (1100 - 1000 - 100) / (1000 + 0*100) = 0
        Math.Abs(result.Rate!.Value).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public void Returns_null_when_denominator_is_zero()
    {
        // No starting balance, no flows → denominator = 0.
        var ctx = Ctx(starting: 0m, ending: 100m);

        var result = _calc.Compute(ctx);

        result.Rate.ShouldBeNull();
        result.Reason.ShouldBe("ZeroDenominator");
    }

    [Fact]
    public void Withdrawal_reduces_net_and_denominator()
    {
        // BMV=1000, EMV=1000, -100 at midpoint.
        // Investor took out 100 halfway; remaining balance grew back to 1000.
        var midpoint = Start.AddDays(182);
        var ctx = Ctx(
            starting: 1000m,
            ending: 1000m,
            flows: new[] { new CashFlow(midpoint, -100m) });

        var result = _calc.Compute(ctx);

        // R = (1000 - 1000 - (-100)) / (1000 + weight * -100)
        //   = 100 / (1000 - 100 * (365-182)/365)
        var expectedDenom = 1000m + -100m * (365m - 182m) / 365m;
        var expected = 100m / expectedDenom;
        Math.Abs(result.Rate!.Value - expected).ShouldBeLessThan(0.0001m);
    }

    private static PerformanceComputationContext Ctx(
        decimal starting = 1000m,
        decimal ending = 1000m,
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
