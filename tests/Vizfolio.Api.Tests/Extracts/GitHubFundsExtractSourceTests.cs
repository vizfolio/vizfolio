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
    private const string RootPrefix = "fund-extracts-master";

    [Fact]
    public async Task GetManifestAsync_reads_funds_json_from_tarball()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["funds.json"] = TarballFixture.Utf8("""
                {
                  "schema_version": "0.1",
                  "generated_at": "2026-01-15T12:00:00Z",
                  "funds": [
                    { "series_id": "S000012345", "latest_period": "2024-12-31", "name": "Sample Fund", "registrant_cik": "0000123", "latest_accession": "A-1", "tickers": ["FUND"] }
                  ]
                }
            """)
        });
        using var source = BuildSource(TarballHandler(tarball));

        var manifest = await source.GetManifestAsync();

        manifest.Funds.Count.ShouldBe(1);
        manifest.Funds[0].SeriesId.ShouldBe("S000012345");
        manifest.Funds[0].LatestPeriod.ShouldBe("2024-12-31");
    }

    [Fact]
    public async Task GetSnapshotAsync_decompresses_gzipped_entry_from_tarball()
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
                "name": "Sample Fund",
                "registrant_cik": "0000123",
                "registrant_name": "Sample Registrant",
                "net_assets_usd": 1000000,
                "is_final_filing": false,
                "share_classes": [],
                "monthly_returns": [{ "month": "2024-12", "return_pct": 0.0123, "class_id": null }]
              },
              "holdings": [
                { "weight": 0.05, "name": "Apple Inc.", "ticker": "AAPL", "issuer_cik": "0000320193", "fair_value_usd": 12345.67 }
              ]
            }
            """;

        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["funds.json"] = TarballFixture.Utf8("""{"schema_version":"0.1","generated_at":"2026-01-15T12:00:00Z","funds":[]}"""),
            ["snapshots/S000012345/2024-12-31.json.gz"] = TarballFixture.Gzip(snapshotJson)
        });
        using var source = BuildSource(TarballHandler(tarball));

        var snapshot = await source.GetSnapshotAsync("S000012345", "2024-12-31");

        snapshot.ShouldNotBeNull();
        snapshot.Fund.SeriesId.ShouldBe("S000012345");
        snapshot.Fund.AsOf.ShouldBe(new DateOnly(2024, 12, 31));
        snapshot.Fund.MonthlyReturns!.Single().Month.ShouldBe(new DateOnly(2024, 12, 1));
        snapshot.Holdings!.Single().IssuerCik.ShouldBe("0000320193");
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_null_when_entry_is_absent()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["funds.json"] = TarballFixture.Utf8("""{"schema_version":"0.1","generated_at":"2026-01-15T12:00:00Z","funds":[]}""")
        });
        using var source = BuildSource(TarballHandler(tarball));

        (await source.GetSnapshotAsync("S000000000", "2024-12-31")).ShouldBeNull();
    }

    [Fact]
    public async Task Repeated_calls_reuse_a_single_tarball_download()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["funds.json"] = TarballFixture.Utf8("""{"schema_version":"0.1","generated_at":"2026-01-15T12:00:00Z","funds":[]}""")
        });
        var handler = TarballHandler(tarball);
        using var source = BuildSource(handler);

        await source.GetManifestAsync();
        await source.GetManifestAsync();
        await source.GetSnapshotAsync("S000000000", "2024-12-31");

        handler.Requests.Count.ShouldBe(1);
    }

    private static StubHttpMessageHandler TarballHandler(byte[] tarball) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(tarball)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/x-gzip") }
            }
        });

    private static GitHubFundsExtractSource BuildSource(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new GitHubExtractOptions());
        return new GitHubFundsExtractSource(http, options, NullLogger<GitHubFundsExtractSource>.Instance);
    }
}
