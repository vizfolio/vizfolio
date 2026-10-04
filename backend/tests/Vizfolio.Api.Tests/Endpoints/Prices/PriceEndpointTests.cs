using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Vizfolio.Api.Endpoints.Settings;

namespace Vizfolio.Api.Tests.Endpoints.Prices;

/// <summary>Price status and provider settings over HTTP (roadmap Phase 3.1, 3.6).</summary>
public sealed class PriceEndpointTests : IClassFixture<VizfolioApiFactory>
{
    private readonly HttpClient _client;

    public PriceEndpointTests(VizfolioApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_price_status_reports_the_refresh_and_series()
    {
        var response = await _client.GetAsync("/api/prices/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusDto>();
        body!.Refresh.ShouldNotBeNull();
        body.Series.ShouldNotBeNull();
    }

    [Fact]
    public async Task POST_price_refresh_is_accepted()
    {
        var response = await _client.PostAsync("/api/prices/refresh", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Providers_are_listed_in_fallback_order_and_a_saved_key_is_never_returned()
    {
        var providers = await _client.GetFromJsonAsync<List<PriceProviderResponse>>("/api/settings/price-providers");
        providers!.Select(p => p.Provider).ShouldBe(["Tiingo", "AlphaVantage", "Eodhd", "Stooq"]);
        providers.Single(p => p.Provider == "Stooq").ShouldSatisfyAllConditions(
            p => p.RequiresApiKey.ShouldBeFalse(),
            p => p.ProvidesRawCloses.ShouldBeFalse(),
            p => p.Available.ShouldBeFalse());

        var saved = await _client.PutAsJsonAsync("/api/settings/price-providers/eodhd", new { apiKey = "secret-123" });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await saved.Content.ReadAsStringAsync();
        text.ShouldNotContain("secret-123");
        var provider = await saved.Content.ReadFromJsonAsync<PriceProviderResponse>();
        provider!.Available.ShouldBeTrue();
        provider.KeySource.ShouldBe("Settings");

        var cleared = await _client.PutAsJsonAsync("/api/settings/price-providers/Eodhd", new { apiKey = "" });
        (await cleared.Content.ReadFromJsonAsync<PriceProviderResponse>())!.KeySource.ShouldBe("None");
    }

    [Fact]
    public async Task A_key_for_an_unknown_provider_is_404_and_for_a_keyless_one_400()
    {
        (await _client.PutAsJsonAsync("/api/settings/price-providers/nope", new { apiKey = "x" })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync("/api/settings/price-providers/stooq", new { apiKey = "x" })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed record StatusDto(RefreshDto Refresh, int ProvidersAvailable, List<object> Series);

    private sealed record RefreshDto(bool Running, bool Pending);
}
