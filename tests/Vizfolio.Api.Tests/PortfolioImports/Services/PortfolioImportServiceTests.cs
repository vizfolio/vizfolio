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

    [Fact]
    public async Task ImportAsync_returns_AccountNotFound_when_account_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var service = NewService(ctx);

        var result = await service.ImportAsync(Guid.NewGuid(), Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.AccountNotFound);
    }

    [Fact]
    public async Task ImportAsync_returns_UnsupportedFormat_when_no_parser_matches()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportAsync(account.AccountId, Stream("garbage content\n"), "junk.txt", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.UnsupportedFormat);
    }

    [Fact]
    public async Task ImportAsync_inserts_transactions_from_csv()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.SourceSystem.ShouldBe("CSV");
        result.Inserted.ShouldBe(2);
        result.Skipped.ShouldBe(0);

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
    public async Task ImportAsync_is_idempotent_within_same_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);

        await service.ImportAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);
        var second = await service.ImportAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        second.Inserted.ShouldBe(0);
        second.Skipped.ShouldBe(2);

        var stored = await ctx.Db.AccountTransactions.AsNoTracking()
            .CountAsync(t => t.AccountId == account.AccountId);
        stored.ShouldBe(2);
    }

    [Fact]
    public async Task ImportAsync_dedupes_per_account_not_globally()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var first = await SeedAccountAsync(ctx, accountNumber: "1111");
        var second = await SeedAccountAsync(ctx, accountNumber: "2222");
        var service = NewService(ctx);

        await service.ImportAsync(first.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);
        var result = await service.ImportAsync(second.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        result.Inserted.ShouldBe(2);
        result.Skipped.ShouldBe(0);

        var total = await ctx.Db.AccountTransactions.AsNoTracking().CountAsync();
        total.ShouldBe(4);
    }

    [Fact]
    public async Task ImportAsync_links_transactions_to_security_instrument_when_ticker_matches()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var security = new Security("0000102909", DateTimeOffset.UtcNow);
        security.SetTickers(new[] { "VOO" });
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();

        var service = NewService(ctx);
        await service.ImportAsync(account.AccountId, Stream(CanonicalCsv), "sample.csv", CancellationToken.None);

        var instrument = await ctx.Db.Instruments.AsNoTracking().SingleAsync();
        instrument.Kind.ShouldBe(Vizfolio.Domain.Instruments.InstrumentKind.Security);
        instrument.SecurityId.ShouldBe(security.SecurityId);

        var rows = await ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.AccountId == account.AccountId).ToListAsync();
        rows.ShouldAllBe(t => t.InstrumentId == instrument.InstrumentId);
    }

    [Fact]
    public async Task ImportAsync_drops_unknown_currency_to_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var service = NewService(ctx);
        var csv = "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo\n" +
                  "2026-06-01,Buy,VOO,1,100,-100,0,ZZZ,note\n";

        await service.ImportAsync(account.AccountId, Stream(csv), "sample.csv", CancellationToken.None);

        var row = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        row.CurrencyCode.ShouldBeNull();
    }

    private static PortfolioImportService NewService(TestDbContext ctx)
    {
        var parsers = new IPortfolioFileParser[] { new QfxFileParser(), new CsvFileParser() };
        return new PortfolioImportService(ctx.Db, parsers, NullLogger<PortfolioImportService>.Instance);
    }

    private static async Task<Account> SeedAccountAsync(TestDbContext ctx, string accountNumber = "1234")
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "Fidelity", accountNumber, "Brokerage");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account;
    }

    private static MemoryStream Stream(string content) =>
        new(Encoding.UTF8.GetBytes(content));
}
