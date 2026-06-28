using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;
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

    private static PortfolioImportService NewService(TestDbContext ctx)
    {
        var parsers = new IPortfolioFileParser[] { new QfxFileParser(), new CsvFileParser() };
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
