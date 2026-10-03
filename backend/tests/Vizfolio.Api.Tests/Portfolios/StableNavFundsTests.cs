using Shouldly;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Portfolios.Valuation;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class StableNavFundsTests
{
    [Theory]
    [InlineData("VMFXX")]
    [InlineData("vusxx")]
    [InlineData("SPAXX")]
    [InlineData("SWVXX")]
    public void Known_money_market_tickers_are_stable_nav_without_any_prices(string symbol)
        => StableNavFunds.IsStableNav(symbol, []).ShouldBeTrue();

    [Fact]
    public void A_series_of_one_dollar_closes_is_stable_nav_once_there_are_enough_observations()
    {
        var prices = OneDollarCloses(StableNavFunds.MinObservations);

        StableNavFunds.IsStableNav("UNKNOWN", prices).ShouldBeTrue();
    }

    [Fact]
    public void Too_few_one_dollar_closes_are_not_enough_to_call_a_fund_stable_nav()
    {
        var prices = OneDollarCloses(StableNavFunds.MinObservations - 1);

        StableNavFunds.IsStableNav("UNKNOWN", prices).ShouldBeFalse();
    }

    [Fact]
    public void A_single_close_away_from_one_dollar_rules_out_stable_nav()
    {
        var prices = OneDollarCloses(StableNavFunds.MinObservations)
            .Append(new PricePointData(new DateOnly(2022, 1, 3), 1.01m))
            .ToList();

        StableNavFunds.IsStableNav("UNKNOWN", prices).ShouldBeFalse();
    }

    [Fact]
    public void An_ordinary_fund_with_no_prices_is_not_stable_nav()
        => StableNavFunds.IsStableNav("VTSAX", []).ShouldBeFalse();

    private static List<PricePointData> OneDollarCloses(int count)
        => Enumerable.Range(0, count)
            .Select(i => new PricePointData(new DateOnly(2021, 6, 1).AddDays(i), 1m))
            .ToList();
}
