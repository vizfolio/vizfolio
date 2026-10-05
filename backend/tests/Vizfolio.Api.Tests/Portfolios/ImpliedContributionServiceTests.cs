using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class ImpliedContributionServiceTests
{
    [Fact]
    public async Task Preview_totals_unfunded_purchases_by_year_for_only_the_requested_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Fund account", "vanguard.com", "1111");
        var other = new Account(portfolio.PortfolioId, "Other account", "vanguard.com", "2222");
        ctx.Db.Accounts.AddRange(account, other);
        await ctx.Db.SaveChangesAsync();

        await SeedAsync(ctx, account.AccountId, new DateOnly(2012, 3, 1), TransactionType.Buy, 1000m, 100m);
        await SeedAsync(ctx, account.AccountId, new DateOnly(2012, 9, 4), TransactionType.Buy, 500m, 45m);
        await SeedAsync(ctx, account.AccountId, new DateOnly(2013, 3, 1), TransactionType.Buy, 2000m, 180m);
        await SeedAsync(ctx, other.AccountId, new DateOnly(2012, 3, 1), TransactionType.Buy, 9999m, 1m);

        var service = new ImpliedContributionService(ctx.Db, new AccountValuationLoader(ctx.Db, new ValuationOptions()));
        var preview = await service.PreviewForAccountAsync(portfolio.PortfolioId, account.AccountId, CancellationToken.None);

        preview.ShouldNotBeNull();
        preview.TotalAmount.ShouldBe(3500m);
        preview.Contributions.Count.ShouldBe(3);
        preview.ByYear.Select(y => (y.Year, y.Amount, y.Count)).ShouldBe(new[] { (2012, 1500m, 2), (2013, 2000m, 1) });
        preview.EndingCash.ShouldBe(0m);
    }

    [Fact]
    public async Task Account_outside_the_portfolio_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();

        var service = new ImpliedContributionService(ctx.Db, new AccountValuationLoader(ctx.Db, new ValuationOptions()));

        (await service.PreviewForAccountAsync(portfolio.PortfolioId, Guid.NewGuid(), CancellationToken.None))
            .ShouldBeNull();
    }

    [Fact]
    public async Task Purchases_paid_from_cash_held_before_a_partial_history_are_not_implied()
    {
        // An 18-month QFX: the account already held 5 FUNDX and $1,000 in its settlement fund before the first
        // imported row (a $1,000 purchase). The broker statement (15 FUNDX, $0 cash) shows both, so the purchase
        // was paid from cash the account already had — not new money from outside.
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "vanguard.com", "1111");
        ctx.Db.Accounts.Add(account);
        var fund = new AccountHolding(account.AccountId, AccountHoldingKind.Other);
        fund.SetIdentifiers("FUNDX", name: null, isin: null, cusip: null);
        var settlement = new AccountHolding(account.AccountId, AccountHoldingKind.Other);
        settlement.SetIdentifiers("ZXMXX", name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.AddRange(fund, settlement);
        // ZXMXX is a money market fund per the SEC registry; seen only on the statement, it's the account's cash.
        var settlementFund = new Vizfolio.Domain.Funds.MoneyMarketFund("S000077777");
        settlementFund.Update("Example Money Market Fund", null, "Government", true, 1m, false, new DateOnly(2026, 5, 31), "0000000000-26-000001", ["ZXMXX"]);
        ctx.Db.MoneyMarketFunds.Add(settlementFund);
        await ctx.Db.SaveChangesAsync();

        var buy = new AccountTransaction(account.AccountId, "QFX", "B1", TransactionType.Buy, new DateOnly(2025, 3, 3), -1000m);
        buy.SetSecurityReference("FUNDX", cusip: null);
        buy.SetTradeDetails(10m, price: 100m, fees: null, settlementDate: null);
        buy.LinkToHolding(fund.AccountHoldingId);
        ctx.Db.AccountTransactions.Add(buy);
        var statementDate = new DateOnly(2026, 6, 1);
        var fundSnapshot = new AccountHoldingSnapshot(fund.AccountHoldingId, statementDate, 15m, AccountHoldingSnapshotSource.BrokerPosition);
        fundSnapshot.SetValuation(null, 1650m, 110m, "USD");
        var cashSnapshot = new AccountHoldingSnapshot(settlement.AccountHoldingId, statementDate, 0m, AccountHoldingSnapshotSource.BrokerPosition);
        cashSnapshot.SetValuation(null, 0m, 1m, "USD");
        ctx.Db.AccountHoldingSnapshots.AddRange(fundSnapshot, cashSnapshot);
        await ctx.Db.SaveChangesAsync();

        var service = new ImpliedContributionService(ctx.Db, new AccountValuationLoader(ctx.Db, new ValuationOptions()));
        var preview = await service.PreviewForAccountAsync(portfolio.PortfolioId, account.AccountId, CancellationToken.None);

        preview.ShouldNotBeNull();
        preview.Contributions.ShouldBeEmpty();
        preview.EndingCash.ShouldBe(0m);
    }

    [Theory]
    [InlineData(7, 0)]  // the default window: the deposit clearing a day later paid for the purchases
    [InlineData(0, 1)]  // the strict daily roll: the purchase day looks unfunded
    public async Task Sync_stores_no_implied_contribution_for_purchases_made_while_their_deposit_cleared(
        int settlementDays, int expectedRows)
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "example.com", "1111");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();

        var holiday = new DateOnly(2012, 10, 8);
        await SeedAsync(ctx, account.AccountId, holiday, TransactionType.Buy, -750m, 5m);
        var deposit = new AccountTransaction(account.AccountId, "TEST", "dep-1", TransactionType.Deposit, holiday.AddDays(1), 750m);
        ctx.Db.AccountTransactions.Add(deposit);
        await ctx.Db.SaveChangesAsync();

        var options = new ValuationOptions { ImpliedContributionSettlementDays = settlementDays };
        var service = new ImpliedContributionService(ctx.Db, new AccountValuationLoader(ctx.Db, options));
        var result = await service.SyncForAccountAsync(account.AccountId, CancellationToken.None);

        result.Count.ShouldBe(expectedRows);
        (await service.PreviewForAccountAsync(portfolio.PortfolioId, account.AccountId, CancellationToken.None))!
            .SettlementDays.ShouldBe(settlementDays);
    }

    private static async Task SeedAsync(
        TestDbContext ctx, Guid accountId, DateOnly date, TransactionType type, decimal amount, decimal quantity)
    {
        var tx = new AccountTransaction(accountId, "TEST", Guid.NewGuid().ToString("N"), type, date, amount);
        tx.SetSecurityReference("FUNDX", cusip: null);
        tx.SetTradeDetails(quantity, price: null, fees: null, settlementDate: null);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();
    }
}
