using Shouldly;
using Vizfolio.Application.Pricing;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Pricing;

public sealed class PriceHistorySourceSelectorTests
{
    private static readonly PriceSeriesRequest Request =
        new("AAPL", null, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1));

    [Fact]
    public void Picks_the_highest_priority_supporting_source()
    {
        var stooq = new FakePriceHistorySource { Source = PriceSource.Stooq, Priority = 0 };
        var eodhd = new FakePriceHistorySource { Source = PriceSource.Eodhd, Priority = 10 };
        var selector = new PriceHistorySourceSelector(new IPriceHistorySource[] { stooq, eodhd });

        selector.Select(Request)!.Source.ShouldBe(PriceSource.Eodhd);
    }

    [Fact]
    public void Falls_back_to_the_keyless_source_when_the_api_key_source_is_disabled()
    {
        var stooq = new FakePriceHistorySource { Source = PriceSource.Stooq, Priority = 0 };
        var eodhd = new FakePriceHistorySource { Source = PriceSource.Eodhd, Priority = 10, Enabled = false };
        var selector = new PriceHistorySourceSelector(new IPriceHistorySource[] { stooq, eodhd });

        selector.Select(Request)!.Source.ShouldBe(PriceSource.Stooq);
    }

    [Fact]
    public void Returns_null_when_no_source_supports_the_request()
    {
        var disabled = new FakePriceHistorySource { Enabled = false };
        var selector = new PriceHistorySourceSelector(new IPriceHistorySource[] { disabled });

        selector.Select(Request).ShouldBeNull();
    }
}
