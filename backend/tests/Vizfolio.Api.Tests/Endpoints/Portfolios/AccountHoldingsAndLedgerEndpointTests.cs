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

/// <summary>
/// End-to-end coverage for the account-scoped holdings and ledger read endpoints. Both are
/// exercised over a real import so the assertions reflect what a user would actually see.
/// </summary>
public sealed class AccountHoldingsAndLedgerEndpointTests : IClassFixture<VizfolioApiFactory>
{
    // VOO carries a cost basis (so gain/loss is derivable); AAPL omits it (gain/loss must be null).
    // A $7,500 ACH deposit and a VOO buy give the ledger two transactions of different shapes.
    private const string QfxWithPositions = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>HL-1</ACCTID></INVACCTFROM>
      <INVTRANLIST>
        <DTSTART>20250601</DTSTART><DTEND>20260601</DTEND>
        <INVBANKTRAN>
          <STMTTRN>
            <TRNTYPE>CREDIT</TRNTYPE>
            <DTPOSTED>20251001</DTPOSTED>
            <TRNAMT>7500.00</TRNAMT>
            <FITID>HL-DEP-1</FITID>
            <MEMO>ACH deposit</MEMO>
          </STMTTRN>
          <SUBACCTFUND>CASH</SUBACCTFUND>
        </INVBANKTRAN>
        <BUYSTOCK>
          <INVBUY>
            <INVTRAN><FITID>HL-BUY-1</FITID><DTTRADE>20251015</DTTRADE></INVTRAN>
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

    private readonly HttpClient _client;
    private readonly VizfolioApiFactory _factory;

    public AccountHoldingsAndLedgerEndpointTests(VizfolioApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_holdings_returns_positions_valued_from_latest_snapshot()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        var holdings = await _client.GetFromJsonAsync<List<HoldingResponse>>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/holdings");

        holdings.ShouldNotBeNull();
        holdings.Count.ShouldBe(3); // VOO, AAPL, and the account's cash (listed last)

        var voo = holdings.Single(h => h.Symbol == "VOO");
        voo.HasSnapshot.ShouldBeTrue();
        voo.SnapshotAsOf.ShouldBe(new DateOnly(2026, 6, 1));
        voo.Quantity.ShouldBe(10m);
        voo.MarketValue.ShouldBe(5255.00m);
        voo.CostBasis.ShouldBe(5000.00m);
        voo.GainLoss.ShouldBe(255.00m);
        voo.Status.ShouldBe("Valued");
        voo.ValuationSource.ShouldBe("Snapshot");
        voo.PriceAsOf.ShouldBe(new DateOnly(2026, 6, 1));

        var aapl = holdings.Single(h => h.Symbol == "AAPL");
        aapl.HasSnapshot.ShouldBeTrue();
        aapl.MarketValue.ShouldBe(1000.00m);
        aapl.CostBasis.ShouldBeNull();
        aapl.GainLoss.ShouldBeNull(); // No cost basis → no derived gain/loss.
    }

    [Fact]
    public async Task GET_holdings_before_any_activity_reports_positions_not_yet_held()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        // asOf precedes the history. VOO was bought within it → not held yet, a true $0. AAPL appears on the
        // statement with no trade → held before the history began → unknown there, not $0. No cash yet.
        var holdings = await _client.GetFromJsonAsync<List<HoldingResponse>>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/holdings?asOf=2025-01-01");

        holdings.ShouldNotBeNull();
        holdings.ShouldAllBe(h => !h.HasSnapshot);
        var voo = holdings.Single(h => h.Symbol == "VOO");
        voo.Status.ShouldBe("NotHeld");
        voo.MarketValue.ShouldBe(0m);
        var aapl = holdings.Single(h => h.Symbol == "AAPL");
        aapl.Status.ShouldBe("Missing");
        aapl.MarketValue.ShouldBeNull();
        holdings.ShouldNotContain(h => h.Kind == "Cash"); // no settlement fund and no cash yet → no cash row
    }

    [Fact]
    public async Task GET_holdings_reports_the_accounts_uninvested_cash_as_a_cash_row()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        // $7,500 deposited, $5,000 spent on VOO: $2,500 is still in the account as cash.
        var holdings = await _client.GetFromJsonAsync<List<HoldingResponse>>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/holdings?asOf=2026-06-01");

        var cash = holdings!.Last();
        cash.Kind.ShouldBe("Cash");
        cash.Status.ShouldBe("Valued");
        cash.ValuationSource.ShouldBe("Cash");
        cash.MarketValue.ShouldBe(2500m);
    }

    [Fact]
    public async Task GET_holdings_reports_a_held_position_without_a_price_or_snapshot_as_missing()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        // Between the VOO buy (2025-10-15) and the broker statement (2026-06-01) there's no price for VOO:
        // the 10 shares are known from the ledger, but their value isn't — so it's reported missing, not $0.
        var holdings = await _client.GetFromJsonAsync<List<HoldingResponse>>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/holdings?asOf=2025-12-01");

        var voo = holdings!.Single(h => h.Symbol == "VOO");
        voo.Status.ShouldBe("Missing");
        voo.Quantity.ShouldBe(10m);
        voo.MarketValue.ShouldBeNull();
    }

    [Fact]
    public async Task GET_opening_positions_derives_what_was_held_before_the_history_from_the_statement()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        var body = await _client.GetFromJsonAsync<OpeningPositionsResponse>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/opening-positions");

        // First activity is the 2025-10-01 deposit, so the opening is 2025-09-30. AAPL (5 on the statement, never
        // traded) was held before; VOO was bought within the history; no cash statement exists to check cash.
        body!.AsOf.ShouldBe(new DateOnly(2025, 9, 30));
        var aapl = body.Holdings.Single(h => h.Symbol == "AAPL");
        aapl.Class.ShouldBe("PreHistory");
        aapl.Quantity.ShouldBe(5m);
        aapl.Verified.ShouldBeTrue();
        var voo = body.Holdings.Single(h => h.Symbol == "VOO");
        voo.Class.ShouldBe("None");
        voo.Quantity.ShouldBe(0m);
        body.Cash.Verified.ShouldBeFalse();
    }

    [Fact]
    public async Task GET_holdings_unknown_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio}/accounts/{Guid.NewGuid()}/holdings");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_ledger_returns_transactions_newest_first()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        var ledger = await _client.GetFromJsonAsync<List<LedgerEntryResponse>>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/ledger");

        ledger.ShouldNotBeNull();
        ledger.Count.ShouldBe(2);
        // Buy (2025-10-15) is newer than the deposit (2025-10-01), so it sorts first.
        ledger[0].TradeDate.ShouldBe(new DateOnly(2025, 10, 15));
        ledger[1].TradeDate.ShouldBe(new DateOnly(2025, 10, 1));

        var buy = ledger.Single(t => t.Ticker == "VOO");
        buy.Type.ShouldBe("Buy");
        buy.Quantity.ShouldBe(10m);
        buy.Amount.ShouldBe(-5000.00m);
        buy.AccountHoldingId.ShouldNotBeNull();

        var deposit = ledger.Single(t => t.Memo == "ACH deposit");
        deposit.Type.ShouldBe("Deposit");
        deposit.Amount.ShouldBe(7500.00m);
    }

    [Fact]
    public async Task GET_ledger_filters_by_date_range()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        var ledger = await _client.GetFromJsonAsync<List<LedgerEntryResponse>>(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/ledger?from=2025-10-10");

        ledger.ShouldNotBeNull();
        ledger.Count.ShouldBe(1); // Only the 2025-10-15 buy falls on/after 2025-10-10.
        ledger[0].Ticker.ShouldBe("VOO");
    }

    [Fact]
    public async Task GET_ledger_with_from_after_to_returns_400()
    {
        var (portfolioId, accountId) = await ImportPositionsAsync();

        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolioId}/accounts/{accountId}/ledger?from=2026-06-01&to=2026-01-01");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_ledger_unknown_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/api/portfolios/{portfolio}/accounts/{Guid.NewGuid()}/ledger");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<(Guid PortfolioId, Guid AccountId)> ImportPositionsAsync()
    {
        await EnsureSecuritiesAsync();
        var portfolioId = await CreatePortfolioAsync();

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(QfxWithPositions)), "File", "hl.qfx");
        var import = await _client.PostAsync($"/api/portfolios/{portfolioId}/imports", multipart);
        import.EnsureSuccessStatusCode();
        var body = await import.Content.ReadFromJsonAsync<PortfolioImportResult>();
        return (portfolioId, body!.Accounts[0].AccountId);
    }

    private async Task<Guid> CreatePortfolioAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/portfolios", new CreatePortfolioRequest($"P-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>())!.PortfolioId;
    }

    private async Task EnsureSecuritiesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await UpsertSecurityAsync(db, "0000102909", "VOO");
        await UpsertSecurityAsync(db, "0000320193", "AAPL");
        await db.SaveChangesAsync();
    }

    private static async Task UpsertSecurityAsync(AppDbContext db, string cik, string ticker)
    {
        if (await db.Securities.AnyAsync(s => s.Cik == cik)) return;
        var security = new Security(cik, DateTimeOffset.UtcNow);
        security.SetTickers(new[] { ticker });
        db.Securities.Add(security);
    }
}
