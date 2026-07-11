using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Infrastructure.Extracts;
using Vizfolio.Infrastructure.Extracts.Sources;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class PerFileFundsExtractSourceTests
{
    [Fact]
    public async Task GetManifestAsync_fetches_the_manifest_url()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            req.RequestUri!.ToString().ShouldContain("funds.json");
            return StubHttpMessageHandler.Ok("""
                {
                  "schema_version": "0.1",
                  "generated_at": "2026-01-15T12:00:00Z",
                  "funds": [
                    { "series_id": "S000012345", "latest_period": "2024-12-31", "name": "Sample Fund" }
                  ]
                }
            """);
        });
        using var source = BuildSource(handler);

        var manifest = await source.GetManifestAsync();

        manifest.Funds.Single().SeriesId.ShouldBe("S000012345");
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetSnapshotAsync_substitutes_template_and_decompresses_gzip_body()
    {
        var snapshotJson = """
            {
              "schema_version": "0.4",
              "generated_at": "2026-01-15T12:00:00Z",
              "fund": {
                "series_id": "S000012345",
                "as_of": "2024-12-31",
                "source_filing": "0000123-24-000001",
                "source_url": "https://example.test/source",
                "is_final_filing": false
              },
              "holdings": []
            }
            """;

        var handler = new StubHttpMessageHandler(req =>
        {
            req.RequestUri!.ToString().ShouldContain("S000012345");
            req.RequestUri!.ToString().ShouldContain("2024-12-31");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(TarballFixture.Gzip(snapshotJson))
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                }
            };
        });
        using var source = BuildSource(handler);

        var snapshot = await source.GetSnapshotAsync("S000012345", "2024-12-31");

        snapshot.ShouldNotBeNull();
        snapshot.Fund.SeriesId.ShouldBe("S000012345");
        snapshot.Fund.AsOf.ShouldBe(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_null_on_404()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.NotFound());
        using var source = BuildSource(handler);

        (await source.GetSnapshotAsync("S000000000", "2024-12-31")).ShouldBeNull();
    }

    [Fact]
    public async Task Rate_limiter_throttles_a_burst_of_requests_to_the_configured_rate()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.NotFound());
        using var source = BuildSource(handler, requestsPerSecond: 2);

        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++)
            await source.GetSnapshotAsync($"S{i:D9}", "2024-12-31");
        var elapsed = DateTimeOffset.UtcNow - start;

        // Bucket starts full (2). The 3rd request must wait ~1s for replenishment.
        elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900));
    }

    private static PerFileFundsExtractSource BuildSource(StubHttpMessageHandler handler, int requestsPerSecond = 1000)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new GitHubExtractOptions
        {
            Sources = new ExtractSources
            {
                Funds = new FundsSourceOptions { RequestsPerSecond = requestsPerSecond }
            }
        });
        return new PerFileFundsExtractSource(http, options, NullLogger<PerFileFundsExtractSource>.Instance);
    }
}
