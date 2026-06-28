using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Infrastructure.Extracts;
using Vizfolio.Infrastructure.Extracts.Sources;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class GitHubFundsExtractSourceTests
{
    [Fact]
    public async Task GetManifestAsync_parses_funds_list()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok("""
            {
              "schema_version": "0.1",
              "generated_at": "2026-01-15T12:00:00Z",
              "funds": [
                { "series_id": "S000012345", "latest_period": "2024-12-31", "name": "Sample Fund", "registrant_cik": "0000123", "latest_accession": "A-1", "tickers": ["FUND"] }
              ]
            }
        """));
        var source = BuildSource(handler);

        var manifest = await source.GetManifestAsync();

        manifest.Funds.Count.ShouldBe(1);
        manifest.Funds[0].SeriesId.ShouldBe("S000012345");
        manifest.Funds[0].LatestPeriod.ShouldBe("2024-12-31");
    }

    [Fact]
    public async Task GetSnapshotAsync_decompresses_gzip_payload_and_substitutes_template()
    {
        var body = """
            {
              "schema_version": "0.4",
              "generated_at": "2026-01-15T12:00:00Z",
              "fund": {
                "series_id": "S000012345",
                "as_of": "2024-12-31",
                "source_filing": "0000123-24-000001",
                "source_url": "https://example.test/source",
                "name": "Sample Fund",
                "registrant_cik": "0000123",
                "registrant_name": "Sample Registrant",
                "net_assets_usd": 1000000,
                "is_final_filing": false,
                "share_classes": [{ "class_id": "C000111", "name": "Investor", "ticker": "FUND", "expense_ratio": 0.005 }],
                "monthly_returns": [{ "month": "2024-12-01", "return_pct": 0.0123, "class_id": "C000111" }]
              },
              "holdings": [
                { "weight": 0.05, "name": "Apple Inc.", "ticker": "AAPL", "issuer_cik": "0000320193", "fair_value_usd": 12345.67 }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(req =>
        {
            req.RequestUri!.ToString().ShouldContain("S000012345");
            req.RequestUri!.ToString().ShouldContain("2024-12-31");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(GzipBytes(body))
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                }
            };
        });
        var source = BuildSource(handler);

        var snapshot = await source.GetSnapshotAsync("S000012345", "2024-12-31");

        snapshot.ShouldNotBeNull();
        snapshot.Fund.SeriesId.ShouldBe("S000012345");
        snapshot.Fund.AsOf.ShouldBe(new DateOnly(2024, 12, 31));
        snapshot.Holdings.ShouldNotBeNull();
        snapshot.Holdings.Single().IssuerCik.ShouldBe("0000320193");
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_null_on_404()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.NotFound());
        var source = BuildSource(handler);

        (await source.GetSnapshotAsync("S000000000", "2024-12-31")).ShouldBeNull();
    }

    private static byte[] GzipBytes(string text)
    {
        using var memory = new MemoryStream();
        using (var gz = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new StreamWriter(gz, System.Text.Encoding.UTF8))
        {
            writer.Write(text);
        }
        return memory.ToArray();
    }

    private static GitHubFundsExtractSource BuildSource(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new GitHubExtractOptions());
        return new GitHubFundsExtractSource(http, options, NullLogger<GitHubFundsExtractSource>.Instance);
    }
}
