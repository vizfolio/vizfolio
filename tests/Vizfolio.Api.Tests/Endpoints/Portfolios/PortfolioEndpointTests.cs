using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vizfolio.Api.Endpoints.Portfolios;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Securities;
using Vizfolio.Infrastructure.Persistence;

namespace Vizfolio.Api.Tests.Endpoints.Portfolios;

public sealed class PortfolioEndpointTests : IClassFixture<VizfolioApiFactory>
{
    private const string CanonicalCsv =
        "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo,ExternalId\n" +
        "2026-06-01,Buy,VOO,2,500,-1000.00,0,USD,Buy VOO,ext-1\n" +
        "2026-06-02,Dividend,VOO,,,5.25,,USD,Q2 div,ext-2\n";

    private const string SingleAccountQfxWithPositions = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>PERF-1</ACCTID></INVACCTFROM>
      <INVTRANLIST>
        <DTSTART>20250601</DTSTART><DTEND>20260601</DTEND>
        <BUYSTOCK>
          <INVBUY>
            <INVTRAN><FITID>PERF-BUY-1</FITID><DTTRADE>20251015</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>10</UNITS><UNITPRICE>500.00</UNITPRICE><TOTAL>-5000.00</TOTAL>
            <CURRENCY><CURSYM>USD</CURSYM><CURRATE>1</CURRATE></CURRENCY>
          </INVBUY>
          <BUYTYPE>BUY</BUYTYPE>
        </BUYSTOCK>
      </INVTRANLIST>
      <INVPOSLIST>
        <POSSTOCK>
          <INVPOS>
            <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>10</UNITS><UNITPRICE>525.50</UNITPRICE><MKTVAL>5255.00</MKTVAL><COSTBASIS>5000.00</COSTBASIS>
            <CURRENCY><CURSYM>USD</CURSYM><CURRATE>1</CURRATE></CURRENCY>
          </INVPOS>
        </POSSTOCK>
        <POSSTOCK>
          <INVPOS>
            <SECID><UNIQUEID>AAPL</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>5</UNITS><UNITPRICE>200.00</UNITPRICE><MKTVAL>1000.00</MKTVAL>
            <CURRENCY><CURSYM>USD</CURSYM><CURRATE>1</CURRATE></CURRENCY>
          </INVPOS>
        </POSSTOCK>
      </INVPOSLIST>
    </INVSTMTRS>
  </INVSTMTTRNRS></INVSTMTMSGSRSV1>
</OFX>
""";

    private const string TwoAccountQfx = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1>
    <INVSTMTTRNRS><TRNUID>1</TRNUID>
      <INVSTMTRS>
        <DTASOF>20260601120000</DTASOF>
        <CURDEF>USD</CURDEF>
        <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>E2E-AAA</ACCTID></INVACCTFROM>
        <INVTRANLIST>
          <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
          <BUYSTOCK>
            <INVBUY>
              <INVTRAN><FITID>E2E-A-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>VTSAX</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <UNITS>1</UNITS><UNITPRICE>100.00</UNITPRICE><TOTAL>-100.00</TOTAL>
            </INVBUY>
            <BUYTYPE>BUY</BUYTYPE>
          </BUYSTOCK>
        </INVTRANLIST>
      </INVSTMTRS>
    </INVSTMTTRNRS>
    <INVSTMTTRNRS><TRNUID>2</TRNUID>
      <INVSTMTRS>
        <DTASOF>20260601120000</DTASOF>
        <CURDEF>USD</CURDEF>
        <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>E2E-BBB</ACCTID></INVACCTFROM>
        <INVTRANLIST>
          <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
          <BUYSTOCK>
            <INVBUY>
              <INVTRAN><FITID>E2E-B-1</FITID><DTTRADE>20260120</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <UNITS>3</UNITS><UNITPRICE>400.00</UNITPRICE><TOTAL>-1200.00</TOTAL>
            </INVBUY>
            <BUYTYPE>BUY</BUYTYPE>
          </BUYSTOCK>
        </INVTRANLIST>
      </INVSTMTRS>
    </INVSTMTTRNRS>
  </INVSTMTMSGSRSV1>
</OFX>
""";

    private readonly HttpClient _client;
    private readonly VizfolioApiFactory _factory;

    public PortfolioEndpointTests(VizfolioApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_portfolio_creates_and_returns_201()
    {
        var response = await _client.PostAsJsonAsync("/portfolios", new CreatePortfolioRequest("E2E Portfolio"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        body.ShouldNotBeNull();
        body.PortfolioId.ShouldNotBe(Guid.Empty);
        body.Name.ShouldBe("E2E Portfolio");
        body.AccountCount.ShouldBe(0);
    }

    [Fact]
    public async Task POST_account_under_unknown_portfolio_returns_404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/portfolios/{Guid.NewGuid()}/accounts",
            new CreateAccountRequest(Guid.Empty, "Brokerage", "fidelity.com", "1234", "Brokerage"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_account_duplicate_institution_code_and_number_returns_409()
    {
        var portfolio = await CreatePortfolioAsync();

        var first = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts",
            new CreateAccountRequest(portfolio.PortfolioId, "A1", "fidelity.com", "1234", "Brokerage"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts",
            new CreateAccountRequest(portfolio.PortfolioId, "A2", "Fidelity.COM", "1234", "Brokerage"));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task POST_import_csv_inserts_transactions_and_is_idempotent()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9001");

        var first = await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "sample.csv", CanonicalCsv);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstResult = await first.Content.ReadFromJsonAsync<PortfolioImportResult>();
        firstResult.ShouldNotBeNull();
        firstResult.Accounts.Count.ShouldBe(1);
        firstResult.Accounts[0].Inserted.ShouldBe(2);
        firstResult.Accounts[0].Skipped.ShouldBe(0);
        firstResult.SourceSystem.ShouldBe("CSV");

        var second = await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "sample.csv", CanonicalCsv);
        var secondResult = await second.Content.ReadFromJsonAsync<PortfolioImportResult>();
        secondResult.ShouldNotBeNull();
        secondResult.Accounts[0].Inserted.ShouldBe(0);
        secondResult.Accounts[0].Skipped.ShouldBe(2);
    }

    [Fact]
    public async Task POST_account_import_unsupported_file_returns_415()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9002");

        var response = await UploadAccountFileAsync(
            portfolio.PortfolioId, account.AccountId, "random.txt", "this is not a known format\n");

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task POST_account_import_missing_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await UploadAccountFileAsync(portfolio.PortfolioId, Guid.NewGuid(), "sample.csv", CanonicalCsv);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_account_import_returns_422_when_file_carries_account_metadata()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9003");

        var response = await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "multi.qfx", TwoAccountQfx);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.Status.ShouldBe(PortfolioImportStatus.FileHasAccountInfo);
    }

    [Fact]
    public async Task POST_portfolio_import_creates_accounts_and_buckets_transactions()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await UploadPortfolioFileAsync(portfolio.PortfolioId, "multi.qfx", TwoAccountQfx);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.Status.ShouldBe(PortfolioImportStatus.Success);
        body.Accounts.Count.ShouldBe(2);
        body.Accounts.ShouldAllBe(a => a.Created);
        body.Accounts.Select(a => a.AccountNumber).ShouldBe(new[] { "E2E-AAA", "E2E-BBB" }, ignoreOrder: true);
        body.Accounts.ShouldAllBe(a => a.InstitutionCode == "vanguard.com");
    }

    [Fact]
    public async Task POST_portfolio_import_returns_422_when_file_has_no_account_metadata()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await UploadPortfolioFileAsync(portfolio.PortfolioId, "sample.csv", CanonicalCsv);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.Status.ShouldBe(PortfolioImportStatus.FileHasNoAccountInfo);
    }

    [Fact]
    public async Task POST_portfolio_import_returns_404_when_portfolio_missing()
    {
        var response = await UploadPortfolioFileAsync(Guid.NewGuid(), "multi.qfx", TwoAccountQfx);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_portfolio_performance_returns_ending_balance_from_snapshots_after_import()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();

        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/performance?from=2025-06-01&to=2026-06-01");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioPerformanceResponse>();
        body.ShouldNotBeNull();
        body.From.ShouldBe(new DateOnly(2025, 6, 1));
        body.To.ShouldBe(new DateOnly(2026, 6, 1));
        body.CurrencyCode.ShouldBe("USD");
        body.EndingBalance.Value.ShouldBe(6255.00m); // 5255 (VOO) + 1000 (AAPL)
        body.EndingBalance.IsComplete.ShouldBeTrue();
        body.EndingBalance.SnapshotAsOf.ShouldBe(new DateOnly(2026, 6, 1));
        body.EndingBalance.HoldingsCovered.ShouldBe(2);
        body.EndingBalance.HoldingsMissingSnapshot.ShouldBe(0);
    }

    [Fact]
    public async Task GET_portfolio_performance_partial_history_scenario_starts_at_zero_and_incomplete()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();

        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/performance?from=2025-06-01&to=2026-06-01");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PortfolioPerformanceResponse>();
        body!.StartingBalance.Value.ShouldBe(0m);
        body.StartingBalance.IsComplete.ShouldBeFalse();
        body.StartingBalance.SnapshotAsOf.ShouldBeNull();
        body.StartingBalance.HoldingsMissingSnapshot.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GET_account_performance_scopes_to_a_single_account()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();

        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();
        var importBody = await import.Content.ReadFromJsonAsync<PortfolioImportResult>();
        var accountId = importBody!.Accounts[0].AccountId;

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/performance?from=2025-06-01&to=2026-06-01");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioPerformanceResponse>();
        body!.EndingBalance.Value.ShouldBe(6255.00m);
    }

    [Fact]
    public async Task GET_portfolio_performance_unknown_portfolio_returns_404()
    {
        var response = await _client.GetAsync($"/portfolios/{Guid.NewGuid()}/performance");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_account_performance_unknown_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();
        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts/{Guid.NewGuid()}/performance");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_account_performance_account_in_different_portfolio_returns_404()
    {
        var owningPortfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(owningPortfolio.PortfolioId, accountNumber: "PERF-X");
        var otherPortfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/portfolios/{otherPortfolio.PortfolioId}/accounts/{account.AccountId}/performance");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_portfolio_performance_with_from_after_to_returns_400()
    {
        var portfolio = await CreatePortfolioAsync();
        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/performance?from=2026-06-01&to=2026-01-01");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task EnsurePerfSecuritiesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await UpsertSecurityAsync(db, cik: "0000102909", ticker: "VOO");
        await UpsertSecurityAsync(db, cik: "0000320193", ticker: "AAPL");
        await db.SaveChangesAsync();
    }

    private static async Task UpsertSecurityAsync(AppDbContext db, string cik, string ticker)
    {
        var exists = await db.Securities.AnyAsync(s => s.Cik == cik);
        if (exists) return;
        var security = new Security(cik, DateTimeOffset.UtcNow);
        security.SetTickers(new[] { ticker });
        db.Securities.Add(security);
    }

    private async Task<PortfolioResponse> CreatePortfolioAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/portfolios", new CreatePortfolioRequest($"P-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>())!;
    }

    private async Task<AccountResponse> CreateAccountAsync(Guid portfolioId, string accountNumber)
    {
        var response = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolioId}/accounts",
            new CreateAccountRequest(portfolioId, $"A-{accountNumber}", "fidelity.com", accountNumber, "Brokerage"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private async Task<HttpResponseMessage> UploadAccountFileAsync(Guid portfolioId, Guid accountId, string fileName, string content)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "File", fileName);
        return await _client.PostAsync($"/portfolios/{portfolioId}/accounts/{accountId}/imports", multipart);
    }

    private async Task<HttpResponseMessage> UploadPortfolioFileAsync(Guid portfolioId, string fileName, string content)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "File", fileName);
        return await _client.PostAsync($"/portfolios/{portfolioId}/imports", multipart);
    }
}
