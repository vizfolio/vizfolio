using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;
using Vizfolio.Infrastructure.Pricing;
using Vizfolio.Infrastructure.Pricing.Sources;

namespace Vizfolio.Api.Tests.Pricing;

public sealed class TiingoPriceHistorySourceTests
{
    private const string ApiKey = "test-key";

    private static readonly PriceSeriesRequest Request =
        new("ZXTAX", null, new DateOnly(2020, 8, 28), new DateOnly(2020, 9, 1));

    // Synthetic Tiingo EOD payload: raw close alongside the adjusted close, with a 4-for-1 split on 8/31.
    private const string SamplePayload = """
        [
          { "date": "2020-08-28T00:00:00.000Z", "close": 499.23, "adjClose": 120.10, "divCash": 0.0, "splitFactor": 1.0 },
          { "date": "2020-08-31T00:00:00.000Z", "close": 129.04, "adjClose": 124.81, "divCash": 0.0, "splitFactor": 4.0 },
          { "date": "2020-09-01T00:00:00.000Z", "close": 134.18, "adjClose": 129.78, "divCash": 0.0, "splitFactor": 1.0 }
        ]
        """;

    [Fact]
    public async Task Reads_the_raw_close_not_the_adjusted_close()
    {
        using var source = BuildSource(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok(SamplePayload)));

        var result = await source.GetDailyClosesAsync(Request);

        result.ShouldNotBeNull();
        result.Prices.Select(p => (p.AsOf, p.Close)).ShouldBe(new[]
        {
            (new DateOnly(2020, 8, 28), 499.23m),
            (new DateOnly(2020, 8, 31), 129.04m),
            (new DateOnly(2020, 9, 1), 134.18m),
        });
    }

    [Fact]
    public async Task Turns_a_split_factor_other_than_one_into_a_split_on_that_day()
    {
        using var source = BuildSource(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok(SamplePayload)));

        var result = await source.GetDailyClosesAsync(Request);

        result!.Splits.ShouldHaveSingleItem().ShouldBe(new SplitEvent(new DateOnly(2020, 8, 31), 4m, 1m));
    }

    [Fact]
    public async Task Sends_the_key_in_the_authorization_header_and_never_in_the_url()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok("[]"));
        using var source = BuildSource(handler);

        await source.GetDailyClosesAsync(Request);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Headers.GetValues("Authorization").ShouldHaveSingleItem().ShouldBe($"Token {ApiKey}");
        request.RequestUri!.ToString().ShouldNotContain(ApiKey);
        request.RequestUri.ToString().ShouldBe(
            "https://api.tiingo.com/tiingo/daily/ZXTAX/prices?startDate=2020-08-28&endDate=2020-09-01&format=json");
    }

    [Theory]
    [InlineData("brk.b", "BRK-B")]
    [InlineData("BRK/B", "BRK-B")]
    public async Task Writes_share_class_tickers_with_a_hyphen(string symbol, string expected)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok("[]"));
        using var source = BuildSource(handler);

        await source.GetDailyClosesAsync(Request with { Symbol = symbol });

        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe($"/tiingo/daily/{expected}/prices");
    }

    [Fact]
    public async Task Unknown_ticker_returns_an_empty_series_instead_of_failing_the_import()
    {
        using var source = BuildSource(new StubHttpMessageHandler(_ => StubHttpMessageHandler.NotFound()));

        var result = await source.GetDailyClosesAsync(Request);

        result.ShouldNotBeNull();
        result.Prices.ShouldBeEmpty();
        result.Splits.ShouldBeEmpty();
    }

    [Fact]
    public void Is_disabled_without_an_api_key_and_outranks_the_other_providers_with_one()
    {
        using var keyless = BuildSource(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok("[]")), apiKey: "");
        using var keyed = BuildSource(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok("[]")));

        keyless.Supports(Request).ShouldBeFalse();
        keyed.Supports(Request).ShouldBeTrue();
        keyed.Source.ShouldBe(PriceSource.Tiingo);
        keyed.Priority.ShouldBeGreaterThan(20); // above Alpha Vantage (20), EODHD (10) and Stooq (0)
    }

    private static TiingoPriceHistorySource BuildSource(StubHttpMessageHandler handler, string apiKey = ApiKey)
    {
        var keys = string.IsNullOrEmpty(apiKey) ? new StaticKeyStore() : new StaticKeyStore((PriceSource.Tiingo, apiKey));
        return new TiingoPriceHistorySource(
            new HttpClient(handler), Options.Create(new PriceHistoryOptions()), NullLogger<TiingoPriceHistorySource>.Instance, keys);
    }
}
