using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using Vizfolio.Api.Endpoints.Portfolios;
using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Api.Tests.Endpoints.Portfolios;

public sealed class PortfolioEndpointTests : IClassFixture<VizfolioApiFactory>
{
    private const string CanonicalCsv =
        "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo,ExternalId\n" +
        "2026-06-01,Buy,VOO,2,500,-1000.00,0,USD,Buy VOO,ext-1\n" +
        "2026-06-02,Dividend,VOO,,,5.25,,USD,Q2 div,ext-2\n";

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

    public PortfolioEndpointTests(VizfolioApiFactory factory)
    {
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
