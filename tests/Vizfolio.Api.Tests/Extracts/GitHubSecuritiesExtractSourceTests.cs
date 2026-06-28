using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Infrastructure.Extracts;
using Vizfolio.Infrastructure.Extracts.Sources;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class GitHubSecuritiesExtractSourceTests
{
    [Fact]
    public async Task GetManifestAsync_deserializes_ticker_to_cik_map()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok(
            """{"AAPL":"0000320193","MSFT":"0000789019"}"""));
        var source = BuildSource(handler);

        var manifest = await source.GetManifestAsync();

        manifest.ByTicker["AAPL"].ShouldBe("0000320193");
        manifest.ByTicker["MSFT"].ShouldBe("0000789019");
    }

    [Fact]
    public async Task GetSecurityAsync_substitutes_cik_in_url_template_and_parses_response()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            req.RequestUri!.ToString().ShouldContain("0000320193");
            return StubHttpMessageHandler.Ok("""
                {
                  "cik": "0000320193",
                  "name": "Apple Inc.",
                  "entity_type": "operating",
                  "country": "US",
                  "sector": "Tech",
                  "tickers": ["AAPL"],
                  "exchanges": ["NASDAQ"],
                  "schema_version": "0.1",
                  "source": { "edgar_fetched_at": "2026-01-15T12:00:00Z" }
                }
            """);
        });
        var source = BuildSource(handler);

        var extract = await source.GetSecurityAsync("0000320193");

        extract.ShouldNotBeNull();
        extract.Name.ShouldBe("Apple Inc.");
        extract.Tickers!.ShouldContain("AAPL");
        extract.Source.EdgarFetchedAt.ShouldBe(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetSecurityAsync_returns_null_on_404()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.NotFound());
        var source = BuildSource(handler);

        (await source.GetSecurityAsync("9999999999")).ShouldBeNull();
    }

    private static GitHubSecuritiesExtractSource BuildSource(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new GitHubExtractOptions());
        return new GitHubSecuritiesExtractSource(http, options, NullLogger<GitHubSecuritiesExtractSource>.Instance);
    }
}
