using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class AccountHistoryServiceTests
{
    // ---------- coverage ----------

    [Fact]
    public async Task GetCoverageAsync_returns_null_when_account_is_not_in_the_portfolio()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var otherPortfolioId = await SeedPortfolioAsync(ctx, name: "Other");

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.GetCoverageAsync(otherPortfolioId, accountId, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetCoverageAsync_reports_no_gap_for_empty_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.FirstTransactionDate.ShouldBeNull();
        result.EarliestSnapshotDate.ShouldBeNull();
        result.HasHistoryGap.ShouldBeFalse();
        result.SuggestedOpeningDate.ShouldBeNull();
    }

    [Fact]
    public async Task GetCoverageAsync_flags_gap_when_transactions_precede_earliest_snapshot()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 15));
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2026, 6, 1), source: AccountHoldingSnapshotSource.BrokerPosition);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.FirstTransactionDate.ShouldBe(new DateOnly(2025, 6, 15));
        result.EarliestSnapshotDate.ShouldBe(new DateOnly(2026, 6, 1));
        result.HasHistoryGap.ShouldBeTrue();
        result.SuggestedOpeningDate.ShouldBe(new DateOnly(2025, 6, 14));
        result.BrokerPositionSnapshotCount.ShouldBe(1);
        result.OpeningBalanceSnapshotCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetCoverageAsync_still_flags_gap_when_only_snapshot_has_no_market_value()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 15));
        // A quantity-only opening balance (no market value) yields no usable balance, so the
        // gap must stay open — consistent with the performance starting balance.
        await SeedSnapshotAsync(
            ctx, holding, new DateOnly(2025, 6, 14),
            source: AccountHoldingSnapshotSource.OpeningBalance, marketValue: null);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.HasHistoryGap.ShouldBeTrue();
        result.EarliestSnapshotDate.ShouldBeNull(); // no *valued* snapshot exists
        result.OpeningBalanceSnapshotCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetCoverageAsync_reports_no_gap_when_snapshot_is_at_or_before_first_transaction()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 6, 15), source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 15));

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.HasHistoryGap.ShouldBeFalse();
        result.OpeningBalanceSnapshotCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetCoverageAsync_counts_snapshots_by_source()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 1, 1), source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 6, 30), source: AccountHoldingSnapshotSource.Statement);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2026, 6, 1), source: AccountHoldingSnapshotSource.BrokerPosition);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.OpeningBalanceSnapshotCount.ShouldBe(1);
        result.StatementSnapshotCount.ShouldBe(1);
        result.BrokerPositionSnapshotCount.ShouldBe(1);
    }

    // ---------- opening balance ----------

    [Fact]
    public async Task SetOpeningBalanceAsync_returns_null_when_account_not_in_portfolio()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var otherPortfolioId = await SeedPortfolioAsync(ctx, name: "Other");

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.SetOpeningBalanceAsync(
            otherPortfolioId, accountId, SingleHoldingCommand("VOO"), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_creates_snapshots_for_new_holdings_as_Kind_Other()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            new OpeningBalanceCommand(
                new DateOnly(2025, 1, 1),
                "USD",
                new[]
                {
                    new OpeningBalanceHolding("VOO", Units: 10m, MarketValue: 5000m, UnitPrice: 500m, CostBasis: 4500m, CurrencyCode: null, Cusip: null),
                }),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.SnapshotsCreated.ShouldBe(1);
        result.SnapshotsUpdated.ShouldBe(0);

        var holding = await ctx.Db.AccountHoldings.SingleAsync(h => h.AccountId == accountId);
        holding.Symbol.ShouldBe("VOO");
        holding.Kind.ShouldBe(AccountHoldingKind.Other);
        holding.CurrencyCode.ShouldBe("USD");

        var snapshot = await ctx.Db.AccountHoldingSnapshots.SingleAsync();
        snapshot.AsOf.ShouldBe(new DateOnly(2025, 1, 1));
        snapshot.Quantity.ShouldBe(10m);
        snapshot.MarketValue.ShouldBe(5000m);
        snapshot.CostBasis.ShouldBe(4500m);
        snapshot.UnitPrice.ShouldBe(500m);
        snapshot.CurrencyCode.ShouldBe("USD");
        snapshot.Source.ShouldBe(AccountHoldingSnapshotSource.OpeningBalance);
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_reuses_existing_holding_when_symbol_matches()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var existingHoldingId = await SeedHoldingAsync(ctx, accountId, symbol: "VOO");

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            SingleHoldingCommand("VOO"),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Holdings.Single().AccountHoldingId.ShouldBe(existingHoldingId);
        (await ctx.Db.AccountHoldings.CountAsync(h => h.AccountId == accountId)).ShouldBe(1);
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_replaces_existing_snapshot_at_same_asOf()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holdingId = await SeedHoldingAsync(ctx, accountId, symbol: "VOO");
        await SeedSnapshotAsync(ctx, holdingId, new DateOnly(2025, 1, 1), source: AccountHoldingSnapshotSource.OpeningBalance, marketValue: 4000m);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            new OpeningBalanceCommand(
                new DateOnly(2025, 1, 1),
                "USD",
                new[]
                {
                    new OpeningBalanceHolding("VOO", Units: 10m, MarketValue: 5000m, UnitPrice: 500m, CostBasis: 4500m, CurrencyCode: null, Cusip: null),
                }),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.SnapshotsCreated.ShouldBe(0);
        result.SnapshotsUpdated.ShouldBe(1);
        result.Holdings.Single().Created.ShouldBeFalse();

        var snapshots = await ctx.Db.AccountHoldingSnapshots
            .Where(s => s.AccountHoldingId == holdingId && s.AsOf == new DateOnly(2025, 1, 1))
            .ToListAsync();
        snapshots.Count.ShouldBe(1);
        snapshots.Single().MarketValue.ShouldBe(5000m);
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_derives_market_value_from_units_and_unit_price()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            new OpeningBalanceCommand(
                new DateOnly(2025, 1, 1),
                "USD",
                new[]
                {
                    // Units + unit price, no explicit market value.
                    new OpeningBalanceHolding("VOO", Units: 10m, MarketValue: null, UnitPrice: 500m, CostBasis: null, CurrencyCode: null, Cusip: null),
                }),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Holdings.Single().MarketValue.ShouldBe(5000m);

        var snapshot = await ctx.Db.AccountHoldingSnapshots.SingleAsync();
        snapshot.MarketValue.ShouldBe(5000m); // 10 units × 500
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_leaves_market_value_null_when_neither_value_nor_price_given()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);

        var service = new AccountHistoryService(ctx.Db);
        await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            new OpeningBalanceCommand(
                new DateOnly(2025, 1, 1),
                "USD",
                new[]
                {
                    new OpeningBalanceHolding("VOO", Units: 10m, MarketValue: null, UnitPrice: null, CostBasis: null, CurrencyCode: null, Cusip: null),
                }),
            CancellationToken.None);

        var snapshot = await ctx.Db.AccountHoldingSnapshots.SingleAsync();
        snapshot.MarketValue.ShouldBeNull(); // stays incomplete — no way to value it
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_uses_DefaultCurrencyCode_when_holding_currency_is_absent()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);

        var service = new AccountHistoryService(ctx.Db);
        var result = await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            new OpeningBalanceCommand(
                new DateOnly(2025, 1, 1),
                DefaultCurrencyCode: "EUR",
                new[]
                {
                    new OpeningBalanceHolding("VOO", Units: 10m, MarketValue: 5000m, UnitPrice: null, CostBasis: null, CurrencyCode: null, Cusip: null),
                    new OpeningBalanceHolding("AAPL", Units: 5m, MarketValue: 1000m, UnitPrice: null, CostBasis: null, CurrencyCode: "GBP", Cusip: null),
                }),
            CancellationToken.None);

        result.ShouldNotBeNull();
        var holdings = await ctx.Db.AccountHoldings.Where(h => h.AccountId == accountId).ToListAsync();
        var vooHolding = holdings.Single(h => h.Symbol == "VOO");
        var aaplHolding = holdings.Single(h => h.Symbol == "AAPL");

        var vooSnap = await ctx.Db.AccountHoldingSnapshots.SingleAsync(s => s.AccountHoldingId == vooHolding.AccountHoldingId);
        var aaplSnap = await ctx.Db.AccountHoldingSnapshots.SingleAsync(s => s.AccountHoldingId == aaplHolding.AccountHoldingId);
        vooSnap.CurrencyCode.ShouldBe("EUR");
        aaplSnap.CurrencyCode.ShouldBe("GBP");
    }

    [Fact]
    public async Task SetOpeningBalanceAsync_closes_the_history_gap_flagged_by_coverage()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holdingId = await SeedHoldingAsync(ctx, accountId, symbol: "VOO");
        await SeedTransactionAsync(ctx, accountId, holdingId, new DateOnly(2025, 6, 15));
        await SeedSnapshotAsync(ctx, holdingId, new DateOnly(2026, 6, 1), source: AccountHoldingSnapshotSource.BrokerPosition);

        var service = new AccountHistoryService(ctx.Db);
        var before = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);
        before!.HasHistoryGap.ShouldBeTrue();

        await service.SetOpeningBalanceAsync(
            portfolioId,
            accountId,
            new OpeningBalanceCommand(
                before.SuggestedOpeningDate!.Value,
                "USD",
                new[]
                {
                    new OpeningBalanceHolding("VOO", Units: 10m, MarketValue: 5000m, UnitPrice: 500m, CostBasis: null, CurrencyCode: null, Cusip: null),
                }),
            CancellationToken.None);

        var after = await service.GetCoverageAsync(portfolioId, accountId, CancellationToken.None);
        after!.HasHistoryGap.ShouldBeFalse();
        after.EarliestSnapshotDate.ShouldBe(new DateOnly(2025, 6, 14));
        after.OpeningBalanceSnapshotCount.ShouldBe(1);
    }

    // ---------- helpers ----------

    private static OpeningBalanceCommand SingleHoldingCommand(string symbol) =>
        new(
            new DateOnly(2025, 1, 1),
            "USD",
            new[]
            {
                new OpeningBalanceHolding(symbol, Units: 10m, MarketValue: 5000m, UnitPrice: 500m, CostBasis: null, CurrencyCode: null, Cusip: null),
            });

    private static async Task<Guid> SeedPortfolioAsync(TestDbContext ctx, string name = "Test Portfolio")
    {
        var portfolio = new Portfolio(name);
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio.PortfolioId;
    }

    private static async Task<(Guid PortfolioId, Guid AccountId)> SeedPortfolioWithAccountAsync(TestDbContext ctx)
    {
        var portfolioId = await SeedPortfolioAsync(ctx);
        var account = new Account(portfolioId, "acct 1", "vanguard.com", "1111");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return (portfolioId, account.AccountId);
    }

    private static async Task<Guid> SeedHoldingAsync(TestDbContext ctx, Guid accountId, string? symbol = null)
    {
        var holding = new AccountHolding(accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers(symbol ?? $"SYM{Guid.NewGuid():N}"[..8], null, null, null);
        ctx.Db.AccountHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    private static async Task SeedSnapshotAsync(
        TestDbContext ctx,
        Guid holdingId,
        DateOnly asOf,
        AccountHoldingSnapshotSource source,
        decimal? marketValue = 1000m)
    {
        var snapshot = new AccountHoldingSnapshot(holdingId, asOf, quantity: 1m, source);
        snapshot.SetValuation(costBasis: null, marketValue, unitPrice: null, currencyCode: "USD");
        ctx.Db.AccountHoldingSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedTransactionAsync(
        TestDbContext ctx,
        Guid accountId,
        Guid holdingId,
        DateOnly tradeDate)
    {
        var tx = new AccountTransaction(
            accountId,
            sourceSystem: "TEST",
            externalId: Guid.NewGuid().ToString("N"),
            TransactionType.Buy,
            tradeDate,
            amount: -100m);
        tx.LinkToHolding(holdingId);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();
    }
}
