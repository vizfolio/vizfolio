using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Vizfolio.Api.Endpoints.Health;

namespace Vizfolio.Api.Tests.Endpoints;

public sealed class HealthEndpointTests : IClassFixture<VizfolioApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(VizfolioApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_health_returns_200_with_healthy_status()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        payload.ShouldNotBeNull();
        payload.Status.ShouldBe("Healthy");
    }
}
