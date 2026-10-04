using System.Net;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vizfolio.Api.Endpoints.Portfolios;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;
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
        <INVBANKTRAN>
          <STMTTRN>
            <TRNTYPE>CREDIT</TRNTYPE>
            <DTPOSTED>20251001</DTPOSTED>
            <TRNAMT>7500.00</TRNAMT>
            <FITID>PERF-DEP-1</FITID>
            <MEMO>ACH deposit</MEMO>
          </STMTTRN>
          <SUBACCTFUND>CASH</SUBACCTFUND>
        </INVBANKTRAN>
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
              <SECID><UNIQUEID>ZXTAX</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
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
        var response = await _client.PostAsJsonAsync("/api/portfolios", new CreatePortfolioRequest("E2E Portfolio"));

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
            $"/api/portfolios/{Guid.NewGuid()}/accounts",
            new CreateAccountRequest(Guid.Empty, "Brokerage", "fidelity.com", "1234", "Brokerage"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_account_duplicate_institution_code_and_number_returns_409()
    {
        var portfolio = await CreatePortfolioAsync();

        var first = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts",
            new CreateAccountRequest(portfolio.PortfolioId, "A1", "fidelity.com", "1234", "Brokerage"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts",
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

        // The same file again is recognised and writes nothing; a different file with the same rows dedupes them.
        var second = await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "sample.csv", CanonicalCsv);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondResult = await second.Content.ReadFromJsonAsync<PortfolioImportResult>();
        secondResult.ShouldNotBeNull();
        secondResult.Status.ShouldBe(PortfolioImportStatus.AlreadyImported);
        secondResult.ImportBatchId.ShouldBe(firstResult.ImportBatchId);

        var third = await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "later.csv", CanonicalCsv + "\n");
        var thirdResult = await third.Content.ReadFromJsonAsync<PortfolioImportResult>();
        thirdResult.ShouldNotBeNull();
        thirdResult.Accounts[0].Inserted.ShouldBe(0);
        thirdResult.Accounts[0].Skipped.ShouldBe(2);
    }

    [Fact]
    public async Task GET_import_parsers_lists_registered_parsers_highest_priority_first()
    {
        var parsers = await _client.GetFromJsonAsync<List<ImportParserResponse>>("/api/imports/parsers");

        parsers.ShouldNotBeNull();
        // Vanguard (200) > QFX (100) > generic test CSV (0).
        parsers.Select(p => p.SourceSystem).ShouldBe(new[] { "VANGUARD", "QFX", "CSV" });
        parsers.Single(p => p.SourceSystem == "QFX").FileExtensions.ShouldContain(".qfx");
        parsers.Single(p => p.SourceSystem == "VANGUARD").DisplayName.ShouldBe("Vanguard transaction report");
    }

    [Fact]
    public async Task POST_account_import_honours_explicit_source_system_override()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9010");

        var response = await UploadAccountFileAsync(
            portfolio.PortfolioId, account.AccountId, "ledger.csv", CanonicalCsv, sourceSystem: "CSV");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.SourceSystem.ShouldBe("CSV");
        body.Accounts[0].Inserted.ShouldBe(2);
    }

    [Fact]
    public async Task POST_account_import_unknown_source_system_returns_415()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9011");

        var response = await UploadAccountFileAsync(
            portfolio.PortfolioId, account.AccountId, "ledger.csv", CanonicalCsv, sourceSystem: "does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.Status.ShouldBe(PortfolioImportStatus.UnknownParser);
    }

    [Fact]
    public async Task POST_account_import_vanguard_xlsx_inserts_and_persists_source_type()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9020");

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(BuildVanguardXlsx()), "File", "report.xlsx");
        var response = await _client.PostAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{account.AccountId}/imports", multipart);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.SourceSystem.ShouldBe("VANGUARD");
        body.Accounts[0].Inserted.ShouldBe(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sweep = await db.AccountTransactions.AsNoTracking()
            .SingleAsync(t => t.AccountId == account.AccountId && t.SourceType == "Sweep in");
        sweep.Type.ShouldBe(TransactionType.Other);
    }

    private static byte[] BuildVanguardXlsx()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Transactions");
        string[] headers =
        [
            "Settlement date", "Trade date", "Symbol", "Name", "Type", "Account type",
            "Quantity", "Price", "Commission & fees**", "Amount",
        ];
        for (var c = 0; c < headers.Length; c++) ws.Cell(4, c + 1).Value = headers[c];

        string?[] buy = ["6/1/2026", "6/1/2026", "VOO", "Vanguard S&P 500 ETF", "Buy", "CASH", "2", "$500.0000", "Free", "-$1000.0000"];
        string?[] sweep = ["6/2/2026", "6/2/2026", "ZXMXX", "Settlement Fund", "Sweep in", "CASH", null, null, null, "-$50.0000"];
        for (var c = 0; c < buy.Length; c++) if (buy[c] is { } v) ws.Cell(5, c + 1).Value = v;
        for (var c = 0; c < sweep.Length; c++) if (sweep[c] is { } v) ws.Cell(6, c + 1).Value = v;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
    public async Task POST_account_import_returns_422_when_the_file_is_for_other_accounts()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9003");

        var response = await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "multi.qfx", TwoAccountQfx);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<PortfolioImportResult>();
        body!.Status.ShouldBe(PortfolioImportStatus.AccountMismatch);
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
            $"/api/portfolios/{portfolio.PortfolioId}/performance?from=2025-06-01&to=2026-06-01");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioPerformanceResponse>();
        body.ShouldNotBeNull();
        body.From.ShouldBe(new DateOnly(2025, 6, 1));
        body.To.ShouldBe(new DateOnly(2026, 6, 1));
        body.CurrencyCode.ShouldBe("USD");
        body.EndingBalance.Value.ShouldBe(8755.00m); // 5255 (VOO) + 1000 (AAPL) + 2500 cash (7500 deposited − 5000 VOO)
        body.EndingBalance.IsComplete.ShouldBeTrue();
        body.EndingBalance.SnapshotAsOf.ShouldBe(new DateOnly(2026, 6, 1));
        body.EndingBalance.HoldingsCovered.ShouldBe(3); // VOO, AAPL and the account's cash
        body.EndingBalance.HoldingsMissingSnapshot.ShouldBe(0);
        // The registered strategies: the daily-valued TWR ("investment return") and the XIRR headline ("your return").
        body.Returns.TimeWeighted.Method.ShouldBe("DailyValuedTWR");
        body.Returns.MoneyWeighted.Method.ShouldBe("XIRR");
    }

    [Fact]
    public async Task GET_portfolio_performance_before_the_imported_history_reports_positions_held_before_it_as_unknown()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();

        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();

        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/performance?from=2025-06-01&to=2026-06-01");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PortfolioPerformanceResponse>();

        // The account's first activity is a deposit on 2025-10-01, and the statement shows 5 AAPL that no imported
        // trade bought: AAPL was held before the history starts, so at the 2025-06-01 `from` its value is unknown
        // (not $0). VOO, bought in the history, wasn't held yet. The return is an honest null, with the reason.
        body!.StartingBalance.IsComplete.ShouldBeFalse();
        body.StartingBalance.HoldingsMissingSnapshot.ShouldBe(1);
        var missing = body.StartingBalance.Missing.ShouldHaveSingleItem();
        missing.Symbol.ShouldBe("AAPL");
        missing.Cause.ShouldBe("BeforeHistory");
        body.Returns.TimeWeighted.Rate.ShouldBeNull();
        body.Returns.TimeWeighted.Reason.ShouldBe("IncompleteStartingBalance");

        // The QFX includes a $7500 ACH deposit wrapped in <INVBANKTRAN> — must surface as a Deposit contribution.
        body.Contributions.Net.ShouldBe(7500m);
        body.Contributions.Deposits.ShouldBe(7500m);
        body.Contributions.Withdrawals.ShouldBe(0m);
        body.Contributions.Count.ShouldBe(1);
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
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/performance?from=2025-06-01&to=2026-06-01");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PortfolioPerformanceResponse>();
        body!.EndingBalance.Value.ShouldBe(8755.00m); // positions + the deposit's uninvested $2,500
    }

    [Fact]
    public async Task GET_portfolio_performance_unknown_portfolio_returns_404()
    {
        var response = await _client.GetAsync($"/api/portfolios/{Guid.NewGuid()}/performance");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_account_performance_unknown_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();
        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{Guid.NewGuid()}/performance");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_account_performance_account_in_different_portfolio_returns_404()
    {
        var owningPortfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(owningPortfolio.PortfolioId, accountNumber: "PERF-X");
        var otherPortfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/api/portfolios/{otherPortfolio.PortfolioId}/accounts/{account.AccountId}/performance");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_portfolio_performance_with_from_after_to_returns_400()
    {
        var portfolio = await CreatePortfolioAsync();
        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/performance?from=2026-06-01&to=2026-01-01");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_history_coverage_reports_no_gap_after_import_because_starting_positions_are_derived()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();
        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();
        var accountId = (await import.Content.ReadFromJsonAsync<PortfolioImportResult>())!.Accounts[0].AccountId;

        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/history-coverage");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HistoryCoverageResponse>();
        body.ShouldNotBeNull();
        // The QFX covers only part of the account's life, but its statement (VOO 10, AAPL 5) rolled back over the
        // imported trades gives the starting positions — no manual opening balance is needed.
        body.HasHistoryGap.ShouldBeFalse();
        body.FirstTransactionDate.ShouldNotBeNull();
        body.EarliestSnapshotDate.ShouldBe(new DateOnly(2026, 6, 1));
        body.SuggestedOpeningDate.ShouldBe(body.FirstTransactionDate!.Value.AddDays(-1));
        body.OpeningBalanceSnapshotCount.ShouldBe(0);
        body.BrokerPositionSnapshotCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GET_history_coverage_unknown_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();
        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{Guid.NewGuid()}/history-coverage");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_opening_balance_closes_the_gap_and_unlocks_returns()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();
        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();
        var accountId = (await import.Content.ReadFromJsonAsync<PortfolioImportResult>())!.Accounts[0].AccountId;

        // Snap the suggested opening date from the coverage endpoint, then POST an opening balance
        // that supplies both holdings so the starting balance becomes complete.
        var coverageBefore = await _client.GetFromJsonAsync<HistoryCoverageResponse>(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/history-coverage");
        var openingDate = coverageBefore!.SuggestedOpeningDate!.Value;

        var openingRequest = new SetOpeningBalanceRequest(
            portfolio.PortfolioId,
            accountId,
            openingDate,
            "USD",
            new List<OpeningBalanceHoldingInput>
            {
                new("VOO", Units: 10m, MarketValue: 5000m, UnitPrice: 500m, CostBasis: 4500m, CurrencyCode: null, Cusip: null),
                new("AAPL", Units: 5m, MarketValue: 900m, UnitPrice: 180m, CostBasis: 800m, CurrencyCode: null, Cusip: null),
            });

        var setResponse = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/opening-balance",
            openingRequest);
        setResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setBody = await setResponse.Content.ReadFromJsonAsync<OpeningBalanceResponse>();
        setBody!.SnapshotsCreated.ShouldBe(2);
        setBody.SnapshotsUpdated.ShouldBe(0);

        var coverageAfter = await _client.GetFromJsonAsync<HistoryCoverageResponse>(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/history-coverage");
        coverageAfter!.HasHistoryGap.ShouldBeFalse();
        coverageAfter.EarliestSnapshotDate.ShouldBe(openingDate);
        coverageAfter.OpeningBalanceSnapshotCount.ShouldBe(2);

        // Performance is now honest — starting balance is complete, returns should be populated. The opening
        // balance is the close of openingDate, i.e. the start of a period beginning the next day.
        var perfResponse = await _client.GetFromJsonAsync<PortfolioPerformanceResponse>(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/performance" +
            $"?from={openingDate.AddDays(1):yyyy-MM-dd}&to=2026-06-01");
        perfResponse!.StartingBalance.IsComplete.ShouldBeTrue();
        perfResponse.StartingBalance.Value.ShouldBe(5900m); // 5000 + 900
        perfResponse.Returns.TimeWeighted.Rate.ShouldNotBeNull();
        perfResponse.Returns.MoneyWeighted.Rate.ShouldNotBeNull();
    }

    [Fact]
    public async Task POST_opening_balance_replaces_snapshot_at_same_date_when_called_again()
    {
        await EnsurePerfSecuritiesAsync();
        var portfolio = await CreatePortfolioAsync();
        var import = await UploadPortfolioFileAsync(portfolio.PortfolioId, "perf.qfx", SingleAccountQfxWithPositions);
        import.EnsureSuccessStatusCode();
        var accountId = (await import.Content.ReadFromJsonAsync<PortfolioImportResult>())!.Accounts[0].AccountId;

        var asOf = new DateOnly(2025, 6, 1);
        var first = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/opening-balance",
            new SetOpeningBalanceRequest(portfolio.PortfolioId, accountId, asOf, "USD",
                new List<OpeningBalanceHoldingInput>
                {
                    new("VOO", Units: 10m, MarketValue: 4000m, UnitPrice: 400m, CostBasis: null, CurrencyCode: null, Cusip: null),
                }));
        first.EnsureSuccessStatusCode();
        (await first.Content.ReadFromJsonAsync<OpeningBalanceResponse>())!.SnapshotsCreated.ShouldBe(1);

        var second = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{accountId}/opening-balance",
            new SetOpeningBalanceRequest(portfolio.PortfolioId, accountId, asOf, "USD",
                new List<OpeningBalanceHoldingInput>
                {
                    new("VOO", Units: 10m, MarketValue: 5000m, UnitPrice: 500m, CostBasis: null, CurrencyCode: null, Cusip: null),
                }));
        second.EnsureSuccessStatusCode();
        var secondBody = (await second.Content.ReadFromJsonAsync<OpeningBalanceResponse>())!;
        secondBody.SnapshotsCreated.ShouldBe(0);
        secondBody.SnapshotsUpdated.ShouldBe(1);
        secondBody.Holdings.Single().Created.ShouldBeFalse();
    }

    [Fact]
    public async Task POST_opening_balance_empty_holdings_returns_400()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, "OB-1");

        var response = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{account.AccountId}/opening-balance",
            new SetOpeningBalanceRequest(portfolio.PortfolioId, account.AccountId, new DateOnly(2025, 1, 1), "USD",
                new List<OpeningBalanceHoldingInput>()));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_opening_balance_unknown_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{Guid.NewGuid()}/opening-balance",
            new SetOpeningBalanceRequest(portfolio.PortfolioId, Guid.NewGuid(), new DateOnly(2025, 1, 1), "USD",
                new List<OpeningBalanceHoldingInput>
                {
                    new("VOO", 10m, 5000m, 500m, null, null, null),
                }));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task EnsurePerfSecuritiesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await UpsertSecurityAsync(db, cik: "0000102909", ticker: "VOO");
        await UpsertSecurityAsync(db, cik: "0000320193", ticker: "AAPL");
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GET_imports_lists_an_upload_which_can_then_be_previewed_and_undone_once()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "UNDO-1");
        var upload = await (await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "undo.csv", CanonicalCsv))
            .Content.ReadFromJsonAsync<PortfolioImportResult>();
        var batchId = upload!.ImportBatchId!.Value;

        var history = await _client.GetFromJsonAsync<ImportHistoryDto>($"/api/portfolios/{portfolio.PortfolioId}/imports");
        var listed = history!.Imports.ShouldHaveSingleItem();
        listed.ImportBatchId.ShouldBe(batchId);
        listed.FileName.ShouldBe("undo.csv");
        listed.Status.ShouldBe("Active");
        listed.Accounts.ShouldHaveSingleItem().Inserted.ShouldBe(2);

        var preview = await _client.GetFromJsonAsync<UndoSummaryDto>(
            $"/api/portfolios/{portfolio.PortfolioId}/imports/{batchId}/undo-preview");
        preview!.Transactions.ShouldBe(2);

        var undo = await _client.PostAsync($"/api/portfolios/{portfolio.PortfolioId}/imports/{batchId}/undo", null);
        undo.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await undo.Content.ReadFromJsonAsync<UndoSummaryDto>())!.Transactions.ShouldBe(2);

        var again = await _client.PostAsync($"/api/portfolios/{portfolio.PortfolioId}/imports/{batchId}/undo", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var ledger = await _client.GetFromJsonAsync<List<LedgerEntryResponse>>(
            $"/api/portfolios/{portfolio.PortfolioId}/accounts/{account.AccountId}/ledger");
        ledger.ShouldNotBeNull();
        ledger.ShouldBeEmpty();
    }

    [Fact]
    public async Task GET_import_file_downloads_the_upload_under_its_original_name()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "FILE-1");
        var upload = await (await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "ledger.csv", CanonicalCsv))
            .Content.ReadFromJsonAsync<PortfolioImportResult>();

        var response = await _client.GetAsync($"/api/portfolios/{portfolio.PortfolioId}/imports/{upload!.ImportBatchId}/file");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe(CanonicalCsv);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("ledger.csv");

        (await _client.GetAsync($"/api/portfolios/{Guid.NewGuid()}/imports/{upload.ImportBatchId}/file")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Undo_preview_of_an_unknown_import_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync($"/api/portfolios/{portfolio.PortfolioId}/imports/{Guid.NewGuid()}/undo-preview");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_reprocess_reparses_a_portfolios_stored_files()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "REPRO-1");
        await UploadAccountFileAsync(portfolio.PortfolioId, account.AccountId, "repro.csv", CanonicalCsv);

        var response = await _client.PostAsync($"/api/imports/reprocess?portfolioId={portfolio.PortfolioId}", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReprocessResult>();
        body!.Batches.ShouldBe(1);
        body.Inserted.ShouldBe(0);
        body.Updated.ShouldBe(0);
    }

    private sealed record ImportHistoryDto(List<ImportItemDto> Imports, int TransactionsImportedBeforeHistory);

    private sealed record ImportItemDto(Guid ImportBatchId, string FileName, string Status, List<ImportAccountDto> Accounts);

    private sealed record ImportAccountDto(Guid AccountId, int Inserted);

    private sealed record UndoSummaryDto(Guid ImportBatchId, int Transactions, int Snapshots, int HoldingsRemoved, int AccountsRemoved);

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
            "/api/portfolios", new CreatePortfolioRequest($"P-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>())!;
    }

    private async Task<AccountResponse> CreateAccountAsync(Guid portfolioId, string accountNumber)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/portfolios/{portfolioId}/accounts",
            new CreateAccountRequest(portfolioId, $"A-{accountNumber}", "fidelity.com", accountNumber, "Brokerage"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private async Task<HttpResponseMessage> UploadAccountFileAsync(
        Guid portfolioId, Guid accountId, string fileName, string content, string? sourceSystem = null)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "File", fileName);
        if (sourceSystem is not null)
            multipart.Add(new StringContent(sourceSystem), "SourceSystem");
        return await _client.PostAsync($"/api/portfolios/{portfolioId}/accounts/{accountId}/imports", multipart);
    }

    private async Task<HttpResponseMessage> UploadPortfolioFileAsync(Guid portfolioId, string fileName, string content)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "File", fileName);
        return await _client.PostAsync($"/api/portfolios/{portfolioId}/imports", multipart);
    }
}
