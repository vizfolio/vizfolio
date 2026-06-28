using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Infrastructure.Extracts;
using Vizfolio.Infrastructure.Extracts.Sources;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class GitHubSecuritiesExtractSourceTests
{
    private const string RootPrefix = "securities-extracts-main";

    [Fact]
    public async Task GetManifestAsync_reads_by_ticker_from_tarball()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["by_ticker.json"] = TarballFixture.Utf8("""{"AAPL":"0000320193","MSFT":"0000789019"}""")
        });
        var handler = TarballHandler(tarball);
        using var source = BuildSource(handler);

        var manifest = await source.GetManifestAsync();

        manifest.ByTicker["AAPL"].ShouldBe("0000320193");
        manifest.ByTicker["MSFT"].ShouldBe("0000789019");
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetSecurityAsync_reads_per_cik_file_from_tarball()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["by_ticker.json"] = TarballFixture.Utf8("""{"AAPL":"0000320193"}"""),
            ["by_cik/0000320193.json"] = TarballFixture.Utf8("""
                {
                  "cik": "0000320193",
                  "name": "Apple Inc.",
                  "entity_type": "operating",
                  "country": "US",
                  "sector": "Information Technology",
                  "sic_description": "Electronic Computers",
                  "tickers": ["AAPL"],
                  "exchanges": ["NASDAQ"],
                  "schema_version": "0.1",
                  "source": { "edgar_fetched_at": "2026-01-15T12:00:00Z" }
                }
            """)
        });
        var handler = TarballHandler(tarball);
        using var source = BuildSource(handler);

        var extract = await source.GetSecurityAsync("0000320193");

        extract.ShouldNotBeNull();
        extract.Name.ShouldBe("Apple Inc.");
        extract.SicDescription.ShouldBe("Electronic Computers");
        extract.Source.EdgarFetchedAt.ShouldBe(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Repeated_calls_reuse_a_single_tarball_download()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["by_ticker.json"] = TarballFixture.Utf8("""{"AAPL":"0000320193"}"""),
            ["by_cik/0000320193.json"] = TarballFixture.Utf8("""
                {
                  "cik": "0000320193",
                  "schema_version": "0.1",
                  "source": { "edgar_fetched_at": "2026-01-15T12:00:00Z" }
                }
            """)
        });
        var handler = TarballHandler(tarball);
        using var source = BuildSource(handler);

        await source.GetManifestAsync();
        await source.GetSecurityAsync("0000320193");
        await source.GetSecurityAsync("0000320193");

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetManifestAsync_handles_pax_global_header_entry_from_codeload_tarballs()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["by_ticker.json"] = TarballFixture.Utf8("""{"AAPL":"0000320193"}""")
        }, includePaxGlobalHeader: true);
        using var source = BuildSource(TarballHandler(tarball));

        var manifest = await source.GetManifestAsync();

        manifest.ByTicker["AAPL"].ShouldBe("0000320193");
    }

    [Fact]
    public async Task GetSecurityAsync_returns_null_when_cik_file_is_absent_from_tarball()
    {
        var tarball = TarballFixture.Build(RootPrefix, new Dictionary<string, byte[]>
        {
            ["by_ticker.json"] = TarballFixture.Utf8("""{"AAPL":"0000320193"}""")
        });
        using var source = BuildSource(TarballHandler(tarball));

        (await source.GetSecurityAsync("9999999999")).ShouldBeNull();
    }

    private static StubHttpMessageHandler TarballHandler(byte[] tarball) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(tarball)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/x-gzip") }
            }
        });

    private static GitHubSecuritiesExtractSource BuildSource(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new GitHubExtractOptions());
        return new GitHubSecuritiesExtractSource(http, options, NullLogger<GitHubSecuritiesExtractSource>.Instance);
    }
}
