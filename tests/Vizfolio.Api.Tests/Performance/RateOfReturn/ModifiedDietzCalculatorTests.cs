using Shouldly;
using Vizfolio.Application.Performance.RateOfReturn;

namespace Vizfolio.Api.Tests.Performance.RateOfReturn;

public sealed class ModifiedDietzCalculatorTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2025, 12, 31);

    [Fact]
    public void No_flows_matches_simple_return()
    {
        var result = ModifiedDietzCalculator.Calculate(
            100_000m, 110_000m, Start, End.AddDays(1), Array.Empty<CashFlow>());

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.10m, 0.0001m);
    }

    [Fact]
    public void Mid_period_deposit_is_weighted_at_one_half()
    {
        // 365-day period, deposit at day 182 → weight ~ (365-182)/365 ~ 0.501
        // Denominator: 100k + 0.501 * 50k = 125,068.49
        // Numerator: 160k - 100k - 50k = 10k
        // Return: 10,000 / 125,068.49 = 0.07996
        var midDay = Start.AddDays(182);
        var flows = new[] { new CashFlow(midDay, 50_000m) };

        var result = ModifiedDietzCalculator.Calculate(
            100_000m, 160_000m, Start, End.AddDays(1), flows);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.0800m, 0.001m);
    }

    [Fact]
    public void Flow_on_period_start_is_fully_weighted()
    {
        var flows = new[] { new CashFlow(Start, 100_000m) };

        // Denominator: 100k + 1.0 * 100k = 200k
        // Numerator: 220k - 100k - 100k = 20k
        // Return: 0.10
        var result = ModifiedDietzCalculator.Calculate(
            100_000m, 220_000m, Start, End.AddDays(1), flows);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.10m, 0.001m);
    }

    [Fact]
    public void Flow_on_period_end_carries_zero_weight()
    {
        var flows = new[] { new CashFlow(End.AddDays(1), 100_000m) };

        // Denominator: 100k + 0 * 100k = 100k
        // Numerator: 220k - 100k - 100k = 20k
        // Return: 0.20
        var result = ModifiedDietzCalculator.Calculate(
            100_000m, 220_000m, Start, End.AddDays(1), flows);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.20m, 0.001m);
    }

    [Fact]
    public void Zero_length_period_returns_null()
    {
        var result = ModifiedDietzCalculator.Calculate(
            100m, 100m, Start, Start, Array.Empty<CashFlow>());

        result.ShouldBeNull();
    }

    [Fact]
    public void Withdrawal_flow_works_with_negative_amount()
    {
        var midDay = Start.AddDays(182);
        var flows = new[] { new CashFlow(midDay, -50_000m) };

        // Denominator: 100k + 0.501 * -50k = 74,931.51
        // Numerator: 60k - 100k - (-50k) = 10k
        // Return: 10k / 74,931 ≈ 0.1334
        var result = ModifiedDietzCalculator.Calculate(
            100_000m, 60_000m, Start, End.AddDays(1), flows);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(0.1334m, 0.001m);
    }
}
