using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class PortfolioPerformanceServiceTests
{
    private static readonly DateOnly From = new(2025, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 1);

    [Fact]
    public async Task Account_scope_ending_snapshot_for_every_holding_reports_complete_sum()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var h1 = await SeedHoldingAsync(ctx, accountId);
        var h2 = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, h1, To, marketValue: 1000m);
        await SeedSnapshotAsync(ctx, h2, To, marketValue: 500m);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(1500m);
        result.EndingBalance.IsComplete.ShouldBeTrue();
        result.EndingBalance.SnapshotAsOf.ShouldBe(To);
        result.EndingBalance.HoldingsCovered.ShouldBe(2);
        result.EndingBalance.HoldingsMissingSnapshot.ShouldBe(0);
    }

    [Fact]
    public async Task No_snapshot_before_from_produces_incomplete_zero_starting_balance()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 1), TransactionType.Buy, amount: -500m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 900m);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.Value.ShouldBe(0m);
        result.StartingBalance.IsComplete.ShouldBeFalse();
        result.StartingBalance.SnapshotAsOf.ShouldBeNull();
        result.StartingBalance.HoldingsCovered.ShouldBe(0);
        result.StartingBalance.HoldingsMissingSnapshot.ShouldBe(1);
    }

    [Fact]
    public async Task Portfolio_scope_sums_balances_across_all_accounts()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolioId = await SeedPortfolioAsync(ctx);
        var acct1 = await SeedAccountAsync(ctx, portfolioId, "AAA-111");
        var acct2 = await SeedAccountAsync(ctx, portfolioId, "BBB-222");
        var h1 = await SeedHoldingAsync(ctx, acct1);
        var h2 = await SeedHoldingAsync(ctx, acct2);
        await SeedSnapshotAsync(ctx, h1, To, marketValue: 700m);
        await SeedSnapshotAsync(ctx, h2, To, marketValue: 300m);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForPortfolioAsync(portfolioId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(1000m);
        result.EndingBalance.HoldingsCovered.ShouldBe(2);
    }

    [Fact]
    public async Task Multiple_snapshots_before_to_selects_the_latest_one_at_or_before_to()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 6, 1), marketValue: 500m);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 12, 31), marketValue: 900m); // <= To
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2026, 2, 1), marketValue: 1200m); // > To, ignored

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(900m);
        result.EndingBalance.SnapshotAsOf.ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public async Task Snapshot_with_null_market_value_is_counted_as_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: null);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(0m);
        result.EndingBalance.HoldingsCovered.ShouldBe(0);
        result.EndingBalance.HoldingsMissingSnapshot.ShouldBe(1);
        result.EndingBalance.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task Holding_with_no_activity_and_no_snapshot_is_ignored_from_missing_count()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var active = await SeedHoldingAsync(ctx, accountId);
        var dormant = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, active, To, marketValue: 1000m);
        // dormant holding has no snapshot and no transactions in range — should be excluded entirely.

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        _ = dormant;
        result.EndingBalance.Value.ShouldBe(1000m);
        result.EndingBalance.HoldingsCovered.ShouldBe(1);
        result.EndingBalance.HoldingsMissingSnapshot.ShouldBe(0);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task Holding_active_in_range_without_prior_snapshot_is_missing_for_starting_balance()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 1), TransactionType.Buy, amount: -100m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 150m);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.HoldingsMissingSnapshot.ShouldBe(1);
        result.StartingBalance.IsComplete.ShouldBeFalse();
        result.EndingBalance.HoldingsCovered.ShouldBe(1);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task From_after_to_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 100m);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(
            portfolioId, accountId, new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_portfolio_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var service = new PortfolioPerformanceService(ctx.Db);

        var result = await service.ComputeForPortfolioAsync(Guid.NewGuid(), From, To, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Account_in_a_different_portfolio_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var otherPortfolioId = await SeedPortfolioAsync(ctx, name: "Other");

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(otherPortfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Currency_defaults_to_USD_when_no_snapshots()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, _) = await SeedPortfolioWithAccountAsync(ctx);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForPortfolioAsync(portfolioId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public async Task Currency_selects_the_mode_when_snapshots_disagree()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var h1 = await SeedHoldingAsync(ctx, accountId);
        var h2 = await SeedHoldingAsync(ctx, accountId);
        var h3 = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, h1, To, marketValue: 100m, currency: "USD");
        await SeedSnapshotAsync(ctx, h2, To, marketValue: 100m, currency: "USD");
        await SeedSnapshotAsync(ctx, h3, To, marketValue: 100m, currency: "EUR");

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public async Task Service_does_not_persist_any_changes()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 42m);

        var beforeSnapshots = await ctx.Db.AccountHoldingSnapshots.CountAsync();
        var beforeHoldings = await ctx.Db.AccountHoldings.CountAsync();

        var service = new PortfolioPerformanceService(ctx.Db);
        _ = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);
        _ = await service.ComputeForPortfolioAsync(portfolioId, From, To, CancellationToken.None);

        (await ctx.Db.AccountHoldingSnapshots.CountAsync()).ShouldBe(beforeSnapshots);
        (await ctx.Db.AccountHoldings.CountAsync()).ShouldBe(beforeHoldings);
        ctx.Db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Default_from_falls_back_to_earliest_transaction_date_when_not_supplied()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        var earliestTrade = new DateOnly(2025, 3, 15);
        await SeedTransactionAsync(ctx, accountId, holding, earliestTrade, TransactionType.Buy, amount: -100m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 9, 1), TransactionType.Buy, amount: -100m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 300m);

        var service = new PortfolioPerformanceService(ctx.Db);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.From.ShouldBe(earliestTrade);
    }

    // ---------- seeding helpers ----------

    private static async Task<Guid> SeedPortfolioAsync(TestDbContext ctx, string name = "Test Portfolio")
    {
        var portfolio = new Portfolio(name);
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio.PortfolioId;
    }

    private static async Task<Guid> SeedAccountAsync(TestDbContext ctx, Guid portfolioId, string accountNumber)
    {
        var account = new Account(portfolioId, $"acct {accountNumber}", "vanguard.com", accountNumber);
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account.AccountId;
    }

    private static async Task<(Guid PortfolioId, Guid AccountId)> SeedPortfolioWithAccountAsync(TestDbContext ctx)
    {
        var portfolioId = await SeedPortfolioAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, portfolioId, "1111");
        return (portfolioId, accountId);
    }

    private static async Task<Guid> SeedHoldingAsync(TestDbContext ctx, Guid accountId)
    {
        var holding = new AccountHolding(accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers($"SYM{Guid.NewGuid():N}"[..8], name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    private static async Task SeedSnapshotAsync(
        TestDbContext ctx,
        Guid holdingId,
        DateOnly asOf,
        decimal? marketValue,
        string? currency = "USD",
        AccountHoldingSnapshotSource source = AccountHoldingSnapshotSource.BrokerPosition)
    {
        var snapshot = new AccountHoldingSnapshot(holdingId, asOf, quantity: 1m, source);
        snapshot.SetValuation(costBasis: null, marketValue, unitPrice: null, currency);
        ctx.Db.AccountHoldingSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedTransactionAsync(
        TestDbContext ctx,
        Guid accountId,
        Guid holdingId,
        DateOnly tradeDate,
        TransactionType type,
        decimal amount)
    {
        var tx = new AccountTransaction(
            accountId,
            sourceSystem: "TEST",
            externalId: Guid.NewGuid().ToString("N"),
            type,
            tradeDate,
            amount);
        tx.LinkToHolding(holdingId);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();
    }
}
