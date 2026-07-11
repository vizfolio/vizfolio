using System.Net;
using System.Text.Json;
using Shouldly;

namespace Vizfolio.Api.Tests.Endpoints;

public sealed class SwaggerDocumentTests : IClassFixture<VizfolioApiFactory>
{
    private readonly HttpClient _client;

    public SwaggerDocumentTests(VizfolioApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OpenApi_document_lists_import_and_health_endpoints()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/health", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/admin/imports/securities", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/admin/imports/funds", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/admin/imports/all", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Import_endpoints_carry_summary_text_in_the_spec()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var summary = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/admin/imports/securities")
            .GetProperty("post")
            .GetProperty("summary")
            .GetString();

        summary!.ShouldContain("securities", Case.Insensitive);
    }

    [Fact]
    public async Task Swagger_ui_html_is_served_at_swagger_path()
    {
        var response = await _client.GetAsync("/swagger");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("swagger", Case.Insensitive);
    }
}
