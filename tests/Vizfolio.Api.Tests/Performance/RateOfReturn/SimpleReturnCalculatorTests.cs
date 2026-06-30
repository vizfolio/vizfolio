using Shouldly;
using Vizfolio.Application.Performance.RateOfReturn;

namespace Vizfolio.Api.Tests.Performance.RateOfReturn;

public sealed class SimpleReturnCalculatorTests
{
    [Fact]
    public void Pure_growth_no_flows_returns_unit_change()
    {
        var result = SimpleReturnCalculator.Calculate(100m, 110m, 0m);

        result.ShouldBe(0.10m);
    }

    [Fact]
    public void Deposit_during_period_is_subtracted_from_growth()
    {
        var result = SimpleReturnCalculator.Calculate(100m, 220m, 100m);

        result.ShouldBe(0.20m);
    }

    [Fact]
    public void Withdrawal_during_period_is_subtracted_from_growth()
    {
        var result = SimpleReturnCalculator.Calculate(100m, 60m, -50m);

        result.ShouldBe(0.10m);
    }

    [Fact]
    public void Zero_beginning_balance_returns_null()
    {
        var result = SimpleReturnCalculator.Calculate(0m, 100m, 100m);

        result.ShouldBeNull();
    }
}
