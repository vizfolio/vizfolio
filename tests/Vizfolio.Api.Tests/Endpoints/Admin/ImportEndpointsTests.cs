using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Api.Tests.Endpoints.Admin;

public sealed class ImportEndpointsTests : IClassFixture<VizfolioApiFactory>
{
    private readonly VizfolioApiFactory _factory;
    private readonly HttpClient _client;

    public ImportEndpointsTests(VizfolioApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_securities_import_returns_result_using_fake_source()
    {
        _factory.SecuritiesSource.Manifest["TEST1"] = "0000000001";
        _factory.SecuritiesSource.Securities["0000000001"] = new SecurityExtract(
            "0000000001", "Test", "operating", "US", "Tech", null, null, null,
            ["TEST1"], ["NASDAQ"], "0.1",
            new SecurityExtractSource(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var response = await _client.PostAsJsonAsync("/admin/imports/securities",
            new { tickers = new[] { "TEST1" }, force = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        result.ShouldNotBeNull();
        result.Considered.ShouldBe(1);
        result.Upserted.ShouldBe(1);
    }

    [Fact]
    public async Task POST_returns_409_when_gate_is_held()
    {
        var gate = _factory.Services.GetRequiredService<ImportRunGate>();
        gate.TryAcquire(out var handle).ShouldBeTrue();
        try
        {
            var response = await _client.PostAsJsonAsync("/admin/imports/securities", new { force = false });
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
        finally
        {
            handle!.Dispose();
        }
    }

    [Fact]
    public async Task POST_funds_import_returns_result_using_fake_source()
    {
        _factory.FundsSource.ManifestEntries.Clear();
        _factory.FundsSource.Snapshots.Clear();
        _factory.FundsSource.ManifestEntries.Add(new FundsManifestEntry(
            "S000111111", "2024-12-31", "Endpoint Fund", null, null, null));
        _factory.FundsSource.Snapshots["S000111111/2024-12-31"] = new FundSnapshotExtract(
            "0.4", DateTimeOffset.UtcNow,
            new FundExtract(
                "S000111111", new DateOnly(2024, 12, 31), "filing", "https://example.test/source",
                "Endpoint Fund", null, null, null, null, null, null, false, null, null),
            Holdings: []);

        var response = await _client.PostAsJsonAsync("/admin/imports/funds",
            new { seriesIds = new[] { "S000111111" }, force = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        result.ShouldNotBeNull();
        result.Upserted.ShouldBe(1);
    }
}
