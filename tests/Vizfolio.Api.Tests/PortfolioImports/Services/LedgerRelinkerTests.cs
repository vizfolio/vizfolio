using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Instruments;
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
    public async Task RelinkAsync_links_unlinked_transactions_to_security_and_creates_instrument()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var tx = NewTransaction(account.AccountId, "ext-1");
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
        stored.InstrumentId.ShouldNotBeNull();

        var instrument = await ctx.Db.Instruments.AsNoTracking().SingleAsync();
        instrument.InstrumentId.ShouldBe(stored.InstrumentId!.Value);
        instrument.Kind.ShouldBe(InstrumentKind.Security);
        instrument.SecurityId.ShouldBe(security.SecurityId);
        instrument.Symbol.ShouldBe("VOO");
    }

    [Fact]
    public async Task RelinkAsync_links_transaction_to_fund_via_latest_snapshot_share_class_ticker()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var tx = NewTransaction(account.AccountId, "ext-1");
        tx.SetSecurityReference("VFIAX", null);
        ctx.Db.AccountTransactions.Add(tx);

        var fund = new Fund("S000001234");
        ctx.Db.Funds.Add(fund);
        await ctx.Db.SaveChangesAsync();

        var older = new FundSnapshot(fund.FundId, new DateOnly(2025, 12, 31), "filing-old", "https://e/old");
        older.ReplaceShareClasses(new[] { new ShareClass("CLS1", "Investor", "OLDTKR", 0.0004m) });
        var latest = new FundSnapshot(fund.FundId, new DateOnly(2026, 6, 1), "filing-new", "https://e/new");
        latest.ReplaceShareClasses(new[] { new ShareClass("CLS1", "Admiral", "VFIAX", 0.0004m) });
        ctx.Db.FundSnapshots.AddRange(older, latest);
        await ctx.Db.SaveChangesAsync();

        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(1);
        var stored = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        stored.InstrumentId.ShouldNotBeNull();

        var instrument = await ctx.Db.Instruments.AsNoTracking().SingleAsync();
        instrument.InstrumentId.ShouldBe(stored.InstrumentId!.Value);
        instrument.Kind.ShouldBe(InstrumentKind.Fund);
        instrument.FundId.ShouldBe(fund.FundId);
        instrument.Symbol.ShouldBe("VFIAX");
    }

    [Fact]
    public async Task RelinkAsync_prefers_security_when_ticker_matches_both_security_and_fund()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var tx = NewTransaction(account.AccountId, "ext-1");
        tx.SetSecurityReference("DUPE", null);
        ctx.Db.AccountTransactions.Add(tx);

        var security = new Security("0000123456", DateTimeOffset.UtcNow);
        security.SetTickers(new[] { "DUPE" });
        ctx.Db.Securities.Add(security);

        var fund = new Fund("S000999999");
        ctx.Db.Funds.Add(fund);
        await ctx.Db.SaveChangesAsync();

        var snapshot = new FundSnapshot(fund.FundId, new DateOnly(2026, 6, 1), "filing", "https://e/x");
        snapshot.ReplaceShareClasses(new[] { new ShareClass("CLS1", "Class", "DUPE", 0.001m) });
        ctx.Db.FundSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();

        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(1);
        var instrument = await ctx.Db.Instruments.AsNoTracking().SingleAsync();
        instrument.Kind.ShouldBe(InstrumentKind.Security);
        instrument.SecurityId.ShouldBe(security.SecurityId);
        instrument.FundId.ShouldBeNull();
    }

    [Fact]
    public async Task RelinkAsync_is_idempotent_and_reuses_existing_instrument()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var firstTx = NewTransaction(account.AccountId, "ext-1");
        firstTx.SetSecurityReference("VOO", null);
        ctx.Db.AccountTransactions.Add(firstTx);

        var security = new Security("0000102909", DateTimeOffset.UtcNow);
        security.SetTickers(new[] { "VOO" });
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();

        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        (await relinker.RelinkAsync()).ShouldBe(1);

        var secondTx = NewTransaction(account.AccountId, "ext-2");
        secondTx.SetSecurityReference("VOO", null);
        ctx.Db.AccountTransactions.Add(secondTx);
        await ctx.Db.SaveChangesAsync();

        var rerunLinked = await relinker.RelinkAsync();
        rerunLinked.ShouldBe(1);

        var instruments = await ctx.Db.Instruments.AsNoTracking().ToListAsync();
        instruments.Count.ShouldBe(1);

        var transactions = await ctx.Db.AccountTransactions.AsNoTracking().ToListAsync();
        transactions.ShouldAllBe(t => t.InstrumentId == instruments[0].InstrumentId);
    }

    [Fact]
    public async Task RelinkAsync_leaves_transaction_unlinked_when_no_match_found()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);

        var tx = NewTransaction(account.AccountId, "ext-1");
        tx.SetSecurityReference("MYSTERY", null);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();

        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(0);
        var stored = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        stored.InstrumentId.ShouldBeNull();
        (await ctx.Db.Instruments.AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    private static AccountTransaction NewTransaction(Guid accountId, string externalId) =>
        new(accountId, "CSV", externalId, TransactionType.Buy, new DateOnly(2026, 6, 1), -100m);

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
