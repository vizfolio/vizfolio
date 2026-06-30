using Shouldly;
using Vizfolio.Application.Performance.RateOfReturn;

namespace Vizfolio.Api.Tests.Performance.RateOfReturn;

public sealed class TimeWeightedReturnCalculatorTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2025, 12, 31);

    [Fact]
    public void No_flows_matches_simple_return()
    {
        var result = TimeWeightedReturnCalculator.Calculate(
            100m, 110m, Start, End, Array.Empty<CashFlow>(), _ => 110m);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.10m, 0.0001m);
    }

    [Fact]
    public void Mid_period_deposit_neutralised_by_chaining()
    {
        // Sub-period 1: 100 → 110 (10% return), then deposit of 100 at mid
        // After deposit value should be 210; sub-period 2: 210 → 231 (10% return)
        // TWR = 1.10 * 1.10 - 1 = 0.21
        var midDay = Start.AddDays(180);

        decimal Valuation(DateOnly d)
        {
            if (d < midDay) return 110m;
            if (d == midDay) return 110m;  // value just before flow
            return 231m;
        }

        var flows = new[] { new CashFlow(midDay, 100m) };

        var result = TimeWeightedReturnCalculator.Calculate(
            100m, 231m, Start, End, flows, Valuation);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.21m, 0.001m);
    }

    [Fact]
    public void Zero_beginning_balance_returns_null()
    {
        var result = TimeWeightedReturnCalculator.Calculate(
            0m, 100m, Start, End, Array.Empty<CashFlow>(), _ => 100m);

        result.ShouldBeNull();
    }

    [Fact]
    public void Loss_period_produces_negative_return()
    {
        var result = TimeWeightedReturnCalculator.Calculate(
            100m, 80m, Start, End, Array.Empty<CashFlow>(), _ => 80m);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(-0.20m, 0.0001m);
    }
}
