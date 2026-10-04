using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Portfolios;

/// <summary>Seeds portfolios, accounts, holdings, ledger rows, snapshots and prices for service-level tests.</summary>
internal static class PortfolioSeed
{

    public static async Task<Guid> SeedPortfolioAsync(TestDbContext ctx, string name = "Test Portfolio")
    {
        var portfolio = new Portfolio(name);
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio.PortfolioId;
    }

    public static async Task<Guid> SeedAccountAsync(TestDbContext ctx, Guid portfolioId, string accountNumber)
    {
        var account = new Account(portfolioId, $"acct {accountNumber}", "vanguard.com", accountNumber);
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account.AccountId;
    }

    public static async Task<(Guid PortfolioId, Guid AccountId)> SeedPortfolioWithAccountAsync(TestDbContext ctx)
    {
        var portfolioId = await SeedPortfolioAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, portfolioId, "1111");
        return (portfolioId, accountId);
    }

    public static async Task<Guid> SeedHoldingAsync(TestDbContext ctx, Guid accountId)
    {
        var holding = new AccountHolding(accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers($"SYM{Guid.NewGuid():N}"[..8], name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    public static async Task<Guid> SeedHoldingWithSymbolAsync(TestDbContext ctx, Guid accountId, string symbol)
    {
        var holding = new AccountHolding(accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    public static async Task SeedPriceAsync(
        TestDbContext ctx, string symbol, DateOnly asOf, decimal close, string? currency = "USD")
    {
        ctx.Db.PriceHistories.Add(PriceHistory.ForSymbol(symbol, asOf, close, currency, PriceSource.Stooq));
        await ctx.Db.SaveChangesAsync();
    }

    public static async Task SeedSplitAsync(
        TestDbContext ctx, string symbol, DateOnly exDate, decimal numerator, decimal denominator)
    {
        ctx.Db.CorporateActions.Add(
            CorporateAction.SplitForSymbol(symbol, exDate, numerator, denominator, PriceSource.Eodhd));
        await ctx.Db.SaveChangesAsync();
    }


    public static async Task SeedSnapshotAsync(
        TestDbContext ctx,
        Guid holdingId,
        DateOnly asOf,
        decimal? marketValue,
        string? currency = "USD",
        AccountHoldingSnapshotSource source = AccountHoldingSnapshotSource.BrokerPosition,
        decimal? quantity = null)
    {
        // A $0 snapshot means nothing is held; otherwise default to a nominal 1-unit position.
        var units = quantity ?? (marketValue == 0m ? 0m : 1m);
        var snapshot = new AccountHoldingSnapshot(holdingId, asOf, units, source);
        snapshot.SetValuation(costBasis: null, marketValue, unitPrice: null, currency);
        ctx.Db.AccountHoldingSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
    }

    public static async Task SeedCashAsync(
        TestDbContext ctx, Guid accountId, DateOnly tradeDate, TransactionType type, decimal amount)
    {
        ctx.Db.AccountTransactions.Add(new AccountTransaction(
            accountId, sourceSystem: "TEST", externalId: Guid.NewGuid().ToString("N"), type, tradeDate, amount));
        await ctx.Db.SaveChangesAsync();
    }

    public static async Task SeedTransactionAsync(
        TestDbContext ctx,
        Guid accountId,
        Guid holdingId,
        DateOnly tradeDate,
        TransactionType type,
        decimal amount,
        decimal? quantity = null)
    {
        var tx = new AccountTransaction(
            accountId,
            sourceSystem: "TEST",
            externalId: Guid.NewGuid().ToString("N"),
            type,
            tradeDate,
            amount);
        tx.LinkToHolding(holdingId);
        if (quantity.HasValue)
            tx.SetTradeDetails(quantity, price: null, fees: null, settlementDate: null);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();
    }
}
