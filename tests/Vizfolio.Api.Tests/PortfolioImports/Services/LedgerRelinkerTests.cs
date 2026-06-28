using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Api.Tests.PortfolioImports.Services;

public sealed class LedgerRelinkerTests
{
    [Fact]
    public async Task RelinkAsync_returns_zero_when_nothing_to_link()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);

        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(0);
    }

    [Fact]
    public async Task RelinkAsync_links_unlinked_transactions_by_ticker()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var tx = new AccountTransaction(account.AccountId, "CSV", "ext-1", TransactionType.Buy, new DateOnly(2026, 6, 1), -100m);
        tx.SetSecurityReference("VOO", null);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();

        var security = new Security("0000102909", DateTimeOffset.UtcNow);
        security.SetTickers(new[] { "VOO" });
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();

        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(1);
        var stored = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        stored.SecurityId.ShouldBe(security.SecurityId);
    }

    [Fact]
    public async Task RelinkAsync_leaves_transaction_unlinked_when_no_security_matches()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var tx = new AccountTransaction(account.AccountId, "CSV", "ext-1", TransactionType.Buy, new DateOnly(2026, 6, 1), -100m);
        tx.SetSecurityReference("MYSTERY", null);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();

        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(0);
        var stored = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        stored.SecurityId.ShouldBeNull();
    }

    private static async Task<Account> SeedAccountAsync(TestDbContext ctx)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "Fidelity", "1234");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account;
    }
}
