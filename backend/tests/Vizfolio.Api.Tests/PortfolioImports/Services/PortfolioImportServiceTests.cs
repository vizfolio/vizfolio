using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Api.Tests.PortfolioImports.Fakes;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Api.Tests.PortfolioImports.Services;

public sealed class PortfolioImportServiceTests
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
        <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>AAA-111</ACCTID></INVACCTFROM>
        <INVTRANLIST>
          <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
          <BUYSTOCK>
            <INVBUY>
              <INVTRAN><FITID>A-BUY-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
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
        <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>BBB-222</ACCTID></INVACCTFROM>
        <INVTRANLIST>
          <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
          <BUYSTOCK>
            <INVBUY>
              <INVTRAN><FITID>B-BUY-1</FITID><DTTRADE>20260120</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <UNITS>3</UNITS><UNITPRICE>400.00</UNITPRICE><TOTAL>-1200.00</TOTAL>
            </INVBUY>
            <BUYTYPE>BUY</BUYTYPE>
          </BUYSTOCK>
          <INCOME>
            <INVTRAN><FITID>B-DIV-1</FITID><DTTRADE>20260221</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <INCOMETYPE>DIV</INCOMETYPE>
            <TOTAL>4.20</TOTAL>
          </INCOME>
        </INVTRANLIST>
      </INVSTMTRS>
    </INVSTMTTRNRS>
  </INVSTMTMSGSRSV1>
</OFX>
""";

    [Fact]
    public async Task ImportToAccountAsync_returns_AccountNotFound_when_account_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var service = NewService(ctx);

        var result = await service.ImportToAccountAsync(Guid.NewGuid(), Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.AccountNotFound);
    }

    [Fact]
    public async Task ImportToAccountAsync_returns_UnsupportedFormat_when_no_parser_matches()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToAccountAsync(account.AccountId, Stream("garbage content\n"), "junk.txt", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.UnsupportedFormat);
    }

    [Fact]
    public async Task ImportToAccountAsync_inserts_transactions_from_csv()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToAccountAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.SourceSystem.ShouldBe("CSV");
        result.Accounts.Count.ShouldBe(1);
        var accountResult = result.Accounts[0];
        accountResult.Inserted.ShouldBe(2);
        accountResult.Skipped.ShouldBe(0);
        accountResult.Created.ShouldBeFalse();

        var transactions = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == account.AccountId)
            .OrderBy(t => t.TradeDate).ToListAsync();
        transactions.Count.ShouldBe(2);
        transactions[0].Ticker.ShouldBe("VOO");
        transactions[0].SourceSystem.ShouldBe("CSV");
        transactions[0].ExternalId.ShouldBe("ext-1");
        transactions[0].CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public async Task ImportToAccountAsync_is_idempotent_within_same_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        await service.ImportToAccountAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);
        var second = await service.ImportToAccountAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        second.Accounts[0].Inserted.ShouldBe(0);
        second.Accounts[0].Skipped.ShouldBe(2);

        var stored = await ctx.Db.AccountTransactions.AsNoTracking()
            .CountAsync(t => t.AccountId == account.AccountId);
        stored.ShouldBe(2);
    }

    [Fact]
    public async Task ImportToAccountAsync_dedupes_per_account_not_globally()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var first = await SeedAccountAsync(ctx, accountNumber: "1111");
        var second = await SeedAccountAsync(ctx, accountNumber: "2222");
        var service = NewService(ctx);

        await service.ImportToAccountAsync(first.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);
        var result = await service.ImportToAccountAsync(second.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Accounts[0].Inserted.ShouldBe(2);
        result.Accounts[0].Skipped.ShouldBe(0);

        var total = await ctx.Db.AccountTransactions.AsNoTracking().CountAsync();
        total.ShouldBe(4);
    }

    [Fact]
    public async Task ImportToAccountAsync_links_transactions_to_security_holding_when_ticker_matches()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var security = new Security("0000102909", DateTimeOffset.UtcNow);
        security.SetTickers(new[] { "VOO" });
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportToAccountAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        var holding = await ctx.Db.AccountHoldings.AsNoTracking().SingleAsync();
        holding.AccountId.ShouldBe(account.AccountId);
        holding.Kind.ShouldBe(AccountHoldingKind.Security);
        holding.SecurityId.ShouldBe(security.SecurityId);

        var rows = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == account.AccountId).ToListAsync();
        rows.ShouldAllBe(t => t.AccountHoldingId == holding.AccountHoldingId);
    }

    [Fact]
    public async Task ImportToAccountAsync_creates_separate_holdings_per_account_for_same_ticker()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var first = await SeedAccountAsync(ctx, accountNumber: "1111");
        var second = await SeedAccountAsync(ctx, accountNumber: "2222");

        var security = new Security("0000102909", DateTimeOffset.UtcNow);
        security.SetTickers(new[] { "VOO" });
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportToAccountAsync(first.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);
        await service.ImportToAccountAsync(second.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        var holdings = await ctx.Db.AccountHoldings.AsNoTracking().ToListAsync();
        holdings.Count.ShouldBe(2);
        holdings.Select(h => h.AccountId).ShouldBe(new[] { first.AccountId, second.AccountId }, ignoreOrder: true);
        holdings.ShouldAllBe(h => h.SecurityId == security.SecurityId);

        var firstHolding = holdings.Single(h => h.AccountId == first.AccountId);
        var secondHolding = holdings.Single(h => h.AccountId == second.AccountId);
        var firstRows = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == first.AccountId).ToListAsync();
        var secondRows = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == second.AccountId).ToListAsync();
        firstRows.ShouldAllBe(t => t.AccountHoldingId == firstHolding.AccountHoldingId);
        secondRows.ShouldAllBe(t => t.AccountHoldingId == secondHolding.AccountHoldingId);
    }

    [Fact]
    public async Task ImportToAccountAsync_drops_unknown_currency_to_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);
        var csv = "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo\n" +
                  "2026-06-01,Buy,VOO,1,100,-100,0,ZZZ,note\n";

        await service.ImportToAccountAsync(account.AccountId, Stream(csv), "sample.csv", CancellationToken.None);

        var row = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        row.CurrencyCode.ShouldBeNull();
    }

    [Fact]
    public async Task ImportToAccountAsync_returns_FileHasAccountInfo_when_OFX_carries_account_metadata()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToAccountAsync(account.AccountId, Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.FileHasAccountInfo);
        result.SourceSystem.ShouldBe("QFX");
        result.Accounts.ShouldBeEmpty();

        var stored = await ctx.Db.AccountTransactions.AsNoTracking().CountAsync();
        stored.ShouldBe(0);
    }

    [Fact]
    public async Task ImportToPortfolioAsync_returns_PortfolioNotFound_when_portfolio_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var service = NewService(ctx);

        var result = await service.ImportToPortfolioAsync(Guid.NewGuid(), Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.PortfolioNotFound);
    }

    [Fact]
    public async Task ImportToPortfolioAsync_returns_FileHasNoAccountInfo_for_csv()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.FileHasNoAccountInfo);
        result.SourceSystem.ShouldBe("CSV");
    }

    [Fact]
    public async Task ImportToPortfolioAsync_creates_one_account_per_statement_when_none_exist()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.Accounts.Count.ShouldBe(2);
        result.Accounts.ShouldAllBe(a => a.Created);
        result.Accounts.Select(a => a.AccountNumber).ShouldBe(new[] { "AAA-111", "BBB-222" }, ignoreOrder: true);

        var accounts = await ctx.Db.Accounts.AsNoTracking()
            .Where(a => a.PortfolioId == portfolio.PortfolioId).ToListAsync();
        accounts.Count.ShouldBe(2);
        accounts.ShouldAllBe(a => a.InstitutionCode == "vanguard.com");
        accounts.ShouldAllBe(a => a.Name == $"vanguard.com {a.AccountNumber}");
    }

    [Fact]
    public async Task ImportToPortfolioAsync_buckets_transactions_to_correct_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var service = NewService(ctx);

        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);

        var accounts = await ctx.Db.Accounts.AsNoTracking()
            .Where(a => a.PortfolioId == portfolio.PortfolioId).ToListAsync();
        var a1 = accounts.Single(a => a.AccountNumber == "AAA-111");
        var a2 = accounts.Single(a => a.AccountNumber == "BBB-222");

        var a1Txs = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == a1.AccountId).ToListAsync();
        a1Txs.Count.ShouldBe(1);
        a1Txs[0].ExternalId.ShouldBe("A-BUY-1");
        a1Txs[0].Ticker.ShouldBe("VTSAX");

        var a2Txs = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == a2.AccountId).ToListAsync();
        a2Txs.Count.ShouldBe(2);
        a2Txs.Select(t => t.ExternalId).ShouldBe(new[] { "B-BUY-1", "B-DIV-1" }, ignoreOrder: true);
        a2Txs.ShouldAllBe(t => t.Ticker == "VOO");
    }

    [Fact]
    public async Task ImportToPortfolioAsync_reuses_existing_account_matched_by_institution_code_and_account_number()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var existing = new Account(portfolio.PortfolioId, "My Vanguard", "vanguard.com", "AAA-111");
        ctx.Db.Accounts.Add(existing);
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        var result = await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);

        var existingResult = result.Accounts.Single(a => a.AccountNumber == "AAA-111");
        existingResult.Created.ShouldBeFalse();
        existingResult.AccountId.ShouldBe(existing.AccountId);

        var newResult = result.Accounts.Single(a => a.AccountNumber == "BBB-222");
        newResult.Created.ShouldBeTrue();

        var existingFromDb = await ctx.Db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == existing.AccountId);
        existingFromDb.Name.ShouldBe("My Vanguard");
    }

    [Fact]
    public async Task ImportToPortfolioAsync_is_idempotent_on_re_upload()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var service = NewService(ctx);

        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);
        var second = await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(TwoAccountQfx), "multi.qfx", CancellationToken.None);

        second.Accounts.Count.ShouldBe(2);
        second.Accounts.ShouldAllBe(a => !a.Created);
        second.Accounts.Sum(a => a.Inserted).ShouldBe(0);
        second.Accounts.Sum(a => a.Skipped).ShouldBe(3);

        (await ctx.Db.Accounts.AsNoTracking()
            .CountAsync(a => a.PortfolioId == portfolio.PortfolioId)).ShouldBe(2);
        (await ctx.Db.AccountTransactions.AsNoTracking().CountAsync()).ShouldBe(3);
    }

    private const string QfxWithPositions = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>POS-1</ACCTID></INVACCTFROM>
      <INVTRANLIST>
        <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
        <BUYSTOCK>
          <INVBUY>
            <INVTRAN><FITID>POS-BUY-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
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

    [Fact]
    public async Task ImportToPortfolioAsync_records_snapshot_per_position_with_DTASOF_as_AsOf()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        SeedSecurity(ctx, cik: "0000102909", ticker: "VOO");
        SeedSecurity(ctx, cik: "0000320193", ticker: "AAPL");
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(QfxWithPositions), "pos.qfx", CancellationToken.None);

        var snapshots = await ctx.Db.AccountHoldingSnapshots.AsNoTracking().ToListAsync();
        snapshots.Count.ShouldBe(2);
        snapshots.ShouldAllBe(s => s.AsOf == new DateOnly(2026, 6, 1));
        snapshots.ShouldAllBe(s => s.Source == AccountHoldingSnapshotSource.BrokerPosition);

        var holdings = await ctx.Db.AccountHoldings.AsNoTracking().ToListAsync();
        var vooHolding = holdings.Single(h => h.Symbol == "VOO");
        var aaplHolding = holdings.Single(h => h.Symbol == "AAPL");

        var vooSnap = snapshots.Single(s => s.AccountHoldingId == vooHolding.AccountHoldingId);
        vooSnap.Quantity.ShouldBe(10m);
        vooSnap.UnitPrice.ShouldBe(525.50m);
        vooSnap.MarketValue.ShouldBe(5255.00m);
        vooSnap.CostBasis.ShouldBe(5000.00m);
        vooSnap.CurrencyCode.ShouldBe("USD");

        var aaplSnap = snapshots.Single(s => s.AccountHoldingId == aaplHolding.AccountHoldingId);
        aaplSnap.Quantity.ShouldBe(5m);
        aaplSnap.CostBasis.ShouldBeNull();
    }

    [Fact]
    public async Task ImportToPortfolioAsync_does_not_duplicate_snapshots_on_re_import()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        SeedSecurity(ctx, cik: "0000102909", ticker: "VOO");
        SeedSecurity(ctx, cik: "0000320193", ticker: "AAPL");
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(QfxWithPositions), "pos.qfx", CancellationToken.None);
        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(QfxWithPositions), "pos.qfx", CancellationToken.None);

        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task ImportToPortfolioAsync_creates_holding_for_position_with_no_matching_transaction()
    {
        const string positionOnlyQfx = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>POS-2</ACCTID></INVACCTFROM>
      <INVTRANLIST><DTSTART>20260101</DTSTART><DTEND>20260601</DTEND></INVTRANLIST>
      <INVPOSLIST>
        <POSSTOCK>
          <INVPOS>
            <SECID><UNIQUEID>MSFT</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>7</UNITS><UNITPRICE>400.00</UNITPRICE><MKTVAL>2800.00</MKTVAL>
          </INVPOS>
        </POSSTOCK>
      </INVPOSLIST>
    </INVSTMTRS>
  </INVSTMTTRNRS></INVSTMTMSGSRSV1>
</OFX>
""";
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        SeedSecurity(ctx, cik: "0000789019", ticker: "MSFT");
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(positionOnlyQfx), "msft.qfx", CancellationToken.None);

        (await ctx.Db.AccountTransactions.AsNoTracking().CountAsync()).ShouldBe(0);
        var holding = await ctx.Db.AccountHoldings.AsNoTracking().SingleAsync();
        holding.Symbol.ShouldBe("MSFT");

        var snapshot = await ctx.Db.AccountHoldingSnapshots.AsNoTracking().SingleAsync();
        snapshot.AccountHoldingId.ShouldBe(holding.AccountHoldingId);
        snapshot.Quantity.ShouldBe(7m);
    }

    [Fact]
    public async Task ImportToPortfolioAsync_skips_position_with_unresolvable_ticker()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        // No Security row for VOO / AAPL — positions cannot resolve.
        var service = NewService(ctx);

        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(QfxWithPositions), "unresolved.qfx", CancellationToken.None);

        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ImportToPortfolioAsync_creates_distinct_holdings_and_snapshots_for_share_classes_of_same_fund()
    {
        // VTI (ETF) and VTSAX (mutual fund) are share classes of the same Vanguard fund —
        // same FundId, different tickers. They must produce two AccountHoldings and two snapshots.
        const string shareClassQfx = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>SHARE-1</ACCTID></INVACCTFROM>
      <INVTRANLIST>
        <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
        <BUYMF>
          <INVBUY>
            <INVTRAN><FITID>VTI-BUY</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>VTI</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>10</UNITS><UNITPRICE>250.00</UNITPRICE><TOTAL>-2500.00</TOTAL>
          </INVBUY>
          <BUYTYPE>BUY</BUYTYPE>
        </BUYMF>
        <BUYMF>
          <INVBUY>
            <INVTRAN><FITID>VTSAX-BUY</FITID><DTTRADE>20260116</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>VTSAX</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>20</UNITS><UNITPRICE>125.00</UNITPRICE><TOTAL>-2500.00</TOTAL>
          </INVBUY>
          <BUYTYPE>BUY</BUYTYPE>
        </BUYMF>
      </INVTRANLIST>
      <INVPOSLIST>
        <POSMF>
          <INVPOS>
            <SECID><UNIQUEID>VTI</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>10</UNITS><UNITPRICE>260.00</UNITPRICE><MKTVAL>2600.00</MKTVAL>
          </INVPOS>
        </POSMF>
        <POSMF>
          <INVPOS>
            <SECID><UNIQUEID>VTSAX</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>20</UNITS><UNITPRICE>130.00</UNITPRICE><MKTVAL>2600.00</MKTVAL>
          </INVPOS>
        </POSMF>
      </INVPOSLIST>
    </INVSTMTRS>
  </INVSTMTTRNRS></INVSTMTMSGSRSV1>
</OFX>
""";
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var fund = new Fund("S000VANGUARD");
        ctx.Db.Funds.Add(fund);
        var snapshot = new FundSnapshot(fund.FundId, new DateOnly(2026, 5, 31), "filing", "https://example.test/seed");
        snapshot.ReplaceShareClasses(new[]
        {
            new ShareClass("C1", "ETF Class",       "VTI",   0.03m),
            new ShareClass("C2", "Admiral Class",   "VTSAX", 0.04m),
        });
        ctx.Db.FundSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(shareClassQfx), "shares.qfx", CancellationToken.None);

        var holdings = await ctx.Db.AccountHoldings.AsNoTracking().ToListAsync();
        holdings.Count.ShouldBe(2);
        holdings.Select(h => h.Symbol).ShouldBe(new[] { "VTI", "VTSAX" }, ignoreOrder: true);
        holdings.ShouldAllBe(h => h.FundId == fund.FundId);

        var snapshots = await ctx.Db.AccountHoldingSnapshots.AsNoTracking().ToListAsync();
        snapshots.Count.ShouldBe(2);
        var vti = holdings.Single(h => h.Symbol == "VTI");
        var vtsax = holdings.Single(h => h.Symbol == "VTSAX");
        snapshots.Single(s => s.AccountHoldingId == vti.AccountHoldingId).Quantity.ShouldBe(10m);
        snapshots.Single(s => s.AccountHoldingId == vtsax.AccountHoldingId).Quantity.ShouldBe(20m);

        var txs = await ctx.Db.AccountTransactions.AsNoTracking().ToListAsync();
        txs.Single(t => t.Ticker == "VTI").AccountHoldingId.ShouldBe(vti.AccountHoldingId);
        txs.Single(t => t.Ticker == "VTSAX").AccountHoldingId.ShouldBe(vtsax.AccountHoldingId);
    }

    [Fact]
    public async Task ImportToAccountAsync_csv_does_not_create_snapshots()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        await service.ImportToAccountAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ImportToAccountAsync_uses_requested_parser_when_source_system_is_given()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        // Lower-case value proves the match is case-insensitive.
        var result = await service.ImportToAccountAsync(
            account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None, requestedSourceSystem: "csv");

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.SourceSystem.ShouldBe("CSV");
        result.Accounts[0].Inserted.ShouldBe(2);
    }

    [Fact]
    public async Task ImportToAccountAsync_returns_UnknownParser_and_does_not_fall_back_when_source_system_unregistered()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        // The file is a perfectly good CSV that auto-detection would claim, but an explicit unknown
        // parser key must fail loudly rather than silently falling back to detection.
        var result = await service.ImportToAccountAsync(
            account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None, requestedSourceSystem: "NOPE");

        result.Status.ShouldBe(PortfolioImportStatus.UnknownParser);
        result.SourceSystem.ShouldBe("NOPE");
        (await ctx.Db.AccountTransactions.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ImportToAccountAsync_auto_detect_prefers_higher_priority_parser()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        // Both parsers claim any file; the higher-priority one must win regardless of registration order.
        var generic = new StubParser("GENERIC", priority: 0);
        var specific = new StubParser("SPECIFIC", priority: 500);
        var service = new PortfolioImportService(
            ctx.Db, new IPortfolioFileParser[] { generic, specific }, NullLogger<PortfolioImportService>.Instance);

        var result = await service.ImportToAccountAsync(
            account.AccountId, Stream("anything"), "file.dat", CancellationToken.None);

        result.SourceSystem.ShouldBe("SPECIFIC");
    }

    [Fact]
    public async Task ImportToAccountAsync_dedupes_the_same_transaction_across_source_systems()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        // The same economic buy (identical date/ticker/signed qty/signed amount) appears in two sources
        // with different labels/ids; plus one row unique to each source.
        var shared = Buy("VOO", tradeDate: new DateOnly(2026, 6, 1), quantity: 10m, amount: -5000m);
        var onlyA = Buy("AAPL", tradeDate: new DateOnly(2026, 6, 2), quantity: 5m, amount: -1000m);
        var onlyB = Buy("MSFT", tradeDate: new DateOnly(2026, 6, 3), quantity: 3m, amount: -1200m);

        var parserA = new LedgerStubParser("SRCA", [shared, onlyA]);
        var parserB = new LedgerStubParser("SRCB", [shared, onlyB]);
        var service = new PortfolioImportService(
            ctx.Db, new IPortfolioFileParser[] { parserA, parserB }, NullLogger<PortfolioImportService>.Instance);

        await service.ImportToAccountAsync(account.AccountId, Stream("a"), "a.dat", CancellationToken.None, "SRCA");
        var second = await service.ImportToAccountAsync(account.AccountId, Stream("b"), "b.dat", CancellationToken.None, "SRCB");

        second.Accounts[0].Inserted.ShouldBe(1); // onlyB
        second.Accounts[0].Skipped.ShouldBe(1);  // shared already covered by SRCA
        (await ctx.Db.AccountTransactions.AsNoTracking().CountAsync(t => t.AccountId == account.AccountId)).ShouldBe(3);
    }

    [Fact]
    public async Task ImportToAccountAsync_dedupes_across_sources_even_after_a_db_round_trip_changes_decimal_scale()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        // The class of bug a user hit: the same cash deposit (no ticker/quantity) in both sources, but the
        // amount stored from QFX comes back from SQLite with a different decimal scale than the
        // freshly-parsed Vanguard amount. The fingerprint must ignore scale so the second import dedupes.
        var qfxLike = new ParsedTransaction(
            ExternalId: "FIT-500", Type: TransactionType.Deposit, TradeDate: new DateOnly(2026, 6, 15),
            SettlementDate: null, Ticker: null, Cusip: null, Quantity: null, Price: null,
            Amount: 500.00m, Fees: null, CurrencyCode: null, Memo: null);
        var vanguardLike = new ParsedTransaction(
            ExternalId: null, Type: TransactionType.Deposit, TradeDate: new DateOnly(2026, 6, 15),
            SettlementDate: new DateOnly(2026, 6, 15), Ticker: null, Cusip: null, Quantity: null, Price: null,
            Amount: 500.0000m, Fees: null, CurrencyCode: null, Memo: "To: MY CREDIT UNION", SourceType: "Funds Received");

        var service = new PortfolioImportService(
            ctx.Db,
            new IPortfolioFileParser[] { new LedgerStubParser("QFX", [qfxLike]), new LedgerStubParser("VANGUARD", [vanguardLike]) },
            NullLogger<PortfolioImportService>.Instance);

        await service.ImportToAccountAsync(account.AccountId, Stream("q"), "q.qfx", CancellationToken.None, "QFX");
        var second = await service.ImportToAccountAsync(account.AccountId, Stream("v"), "v.xlsx", CancellationToken.None, "VANGUARD");

        second.Accounts[0].Inserted.ShouldBe(0);
        second.Accounts[0].Skipped.ShouldBe(1);
        (await ctx.Db.AccountTransactions.AsNoTracking().CountAsync(t => t.AccountId == account.AccountId)).ShouldBe(1);
    }

    [Fact]
    public async Task ImportToAccountAsync_keeps_genuine_same_day_duplicates_but_is_idempotent_on_re_upload()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        // Two truly-identical rows in one file are distinct transactions and must both import.
        var dup = Buy("VOO", tradeDate: new DateOnly(2026, 6, 1), quantity: 1m, amount: -100m);
        var parser = new LedgerStubParser("SRCA", [dup, dup]);
        var service = new PortfolioImportService(
            ctx.Db, new IPortfolioFileParser[] { parser }, NullLogger<PortfolioImportService>.Instance);

        var first = await service.ImportToAccountAsync(account.AccountId, Stream("x"), "x.dat", CancellationToken.None, "SRCA");
        first.Accounts[0].Inserted.ShouldBe(2);

        var second = await service.ImportToAccountAsync(account.AccountId, Stream("x"), "x.dat", CancellationToken.None, "SRCA");
        second.Accounts[0].Inserted.ShouldBe(0);
        second.Accounts[0].Skipped.ShouldBe(2);
        (await ctx.Db.AccountTransactions.AsNoTracking().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task ImportToAccountAsync_persists_the_raw_source_type()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var sweep = new ParsedTransaction(
            ExternalId: null, Type: TransactionType.Other, TradeDate: new DateOnly(2026, 4, 3),
            SettlementDate: null, Ticker: null, Cusip: null, Quantity: null, Price: null,
            Amount: -391.54m, Fees: null, CurrencyCode: null, Memo: null, SourceType: "Sweep in");
        var parser = new LedgerStubParser("SRCA", [sweep]);
        var service = new PortfolioImportService(
            ctx.Db, new IPortfolioFileParser[] { parser }, NullLogger<PortfolioImportService>.Instance);

        await service.ImportToAccountAsync(account.AccountId, Stream("x"), "x.dat", CancellationToken.None, "SRCA");

        var row = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        row.SourceType.ShouldBe("Sweep in");
    }

    private static ParsedTransaction Buy(string ticker, DateOnly tradeDate, decimal quantity, decimal amount) =>
        new(ExternalId: null, Type: TransactionType.Buy, TradeDate: tradeDate, SettlementDate: null,
            Ticker: ticker, Cusip: null, Quantity: quantity, Price: null, Amount: amount,
            Fees: null, CurrencyCode: null, Memo: null);

    /// <summary>A metadata-less parser that emits a fixed transaction list. Only an explicit
    /// <c>sourceSystem</c> override selects it (CanParse is false), so tests can drive each source.</summary>
    private sealed class LedgerStubParser(string sourceSystem, IReadOnlyList<ParsedTransaction> transactions)
        : IPortfolioFileParser
    {
        public string SourceSystem { get; } = sourceSystem;
        public string DisplayName => SourceSystem;
        public int Priority => 0;
        public IReadOnlyCollection<string> FileExtensions { get; } = [".dat"];

        public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new ParsedPortfolioFile(
                SourceSystem, [new ParsedAccountStatement(null, null, transactions, [], null)]));
    }

    /// <summary>A parser that claims every file, used to assert auto-detect priority ordering.</summary>
    private sealed class StubParser(string sourceSystem, int priority) : IPortfolioFileParser
    {
        public string SourceSystem { get; } = sourceSystem;
        public string DisplayName => SourceSystem;
        public int Priority { get; } = priority;
        public IReadOnlyCollection<string> FileExtensions { get; } = [".dat"];

        public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new ParsedPortfolioFile(
                SourceSystem,
                [new ParsedAccountStatement(null, null, [], [], null)]));
    }

    private static void SeedSecurity(TestDbContext ctx, string cik, string ticker)
    {
        var security = new Security(cik, DateTimeOffset.UtcNow);
        security.SetTickers(new[] { ticker });
        ctx.Db.Securities.Add(security);
    }

    private static PortfolioImportService NewService(TestDbContext ctx)
    {
        var parsers = new IPortfolioFileParser[]
        {
            new QfxFileParser(),
            new VanguardTransactionHistoryReportParser(),
            new CsvLedgerTestParser(),
        };
        return new PortfolioImportService(ctx.Db, parsers, NullLogger<PortfolioImportService>.Instance);
    }

    private static async Task<Portfolio> SeedPortfolioAsync(TestDbContext ctx)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio;
    }

    private static async Task<Account> SeedAccountAsync(TestDbContext ctx, string accountNumber = "1234")
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "fidelity.com", accountNumber, "Brokerage");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account;
    }

    private static MemoryStream Stream(string content) =>
        new(Encoding.UTF8.GetBytes(content));
}
