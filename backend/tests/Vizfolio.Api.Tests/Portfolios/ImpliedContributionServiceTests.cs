using Shouldly;
using Vizfolio.Api.Tests.Extracts;
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

        var service = new ImpliedContributionService(ctx.Db);
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

        var service = new ImpliedContributionService(ctx.Db);

        (await service.PreviewForAccountAsync(portfolio.PortfolioId, Guid.NewGuid(), CancellationToken.None))
            .ShouldBeNull();
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
