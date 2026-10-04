using Shouldly;
using Vizfolio.Application.Portfolios.Valuation;

namespace Vizfolio.Api.Tests.Portfolios.Valuation;

public sealed class StablePricesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]   // some insurance-product money market funds
    [InlineData(100)]  // a money market ETF
    public void The_registrys_stable_price_wins_even_without_any_closes(decimal registryPrice)
        => StablePrices.Resolve(registryPrice, []).ShouldBe(registryPrice);

    [Fact]
    public void A_fund_not_in_the_registry_with_an_unchanging_price_history_is_stable_at_that_price()
        => StablePrices.Resolve(null, Closes(StablePrices.MinObservations, 10m)).ShouldBe(10m);

    [Fact]
    public void Too_few_unchanging_closes_are_not_enough_to_call_a_price_stable()
        => StablePrices.Resolve(null, Closes(StablePrices.MinObservations - 1, 1m)).ShouldBeNull();

    [Fact]
    public void A_single_close_that_moves_rules_out_a_stable_price()
    {
        var closes = Closes(StablePrices.MinObservations, 1m)
            .Append(new PricePointData(new DateOnly(2022, 1, 3), 1.01m))
            .ToList();

        StablePrices.Resolve(null, closes).ShouldBeNull();
    }

    [Fact]
    public void A_floating_fund_in_the_registry_falls_back_to_its_price_history()
        => StablePrices.Resolve(null, Closes(5, 1.0003m)).ShouldBeNull();

    private static List<PricePointData> Closes(int count, decimal close)
        => Enumerable.Range(0, count).Select(i => new PricePointData(new DateOnly(2021, 6, 1).AddDays(i), close)).ToList();
}
