using Shouldly;
using Vizfolio.Application.Performance.RateOfReturn;

namespace Vizfolio.Api.Tests.Performance.RateOfReturn;

public sealed class InternalRateOfReturnCalculatorTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2025, 12, 31);

    [Fact]
    public void Pure_growth_one_year_returns_simple_rate()
    {
        // Begin 100, End 110, no flows, ~365 days → annualised ~10%
        var result = InternalRateOfReturnCalculator.Calculate(
            100m, 110m, Start, End, Array.Empty<CashFlow>());

        result.ShouldNotBeNull();
        ((double)result.Value).ShouldBe(0.10, 0.005);
    }

    [Fact]
    public void Mid_period_deposit_does_not_count_as_growth()
    {
        // Begin 100, deposit 100 at midyear, End 200 → essentially flat → IRR ~ 0%
        var midDay = Start.AddDays(182);
        var flows = new[] { new CashFlow(midDay, 100m) };

        var result = InternalRateOfReturnCalculator.Calculate(
            100m, 200m, Start, End, flows);

        result.ShouldNotBeNull();
        ((double)result.Value).ShouldBe(0.0, 0.01);
    }

    [Fact]
    public void All_same_sign_flows_returns_null()
    {
        // Begin 0 (no negative cashflow at start), all deposits → no IRR solvable.
        var flows = new[]
        {
            new CashFlow(Start, 100m),
            new CashFlow(Start.AddDays(100), 100m)
        };

        var result = InternalRateOfReturnCalculator.Calculate(
            0m, 0m, Start, End, flows);

        result.ShouldBeNull();
    }

    [Fact]
    public void Zero_length_period_returns_null()
    {
        var result = InternalRateOfReturnCalculator.Calculate(
            100m, 110m, Start, Start, Array.Empty<CashFlow>());

        result.ShouldBeNull();
    }
}
