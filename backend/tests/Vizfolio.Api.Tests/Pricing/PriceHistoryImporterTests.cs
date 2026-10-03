using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Pricing;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Pricing;

public sealed class PriceHistoryImporterTests
{
    private static readonly DateOnly Buy = new(2025, 1, 1);

    [Fact]
    public async Task Imports_prices_and_splits_keyed_by_symbol_then_dedupes_on_re_run()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");

        var source = new FakePriceHistorySource
        {
            Handler = _ => new PriceSeriesResult(
                new[]
                {
                    new PricePoint(new DateOnly(2025, 1, 2), 100m),
                    new PricePoint(new DateOnly(2025, 1, 3), 110m),
                },
                new[] { new SplitEvent(new DateOnly(2025, 2, 1), 2m, 1m) },
                CurrencyCode: "USD"),
        };
        var importer = NewImporter(ctx, source);

        var first = await importer.ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        first.Upserted.ShouldBe(2);
        (await ctx.Db.PriceHistories.CountAsync()).ShouldBe(2);
        (await ctx.Db.PriceHistories.AllAsync(p => p.SymbolKey == "AAPL")).ShouldBeTrue();
        (await ctx.Db.CorporateActions.CountAsync()).ShouldBe(1);

        var second = await importer.ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        second.Upserted.ShouldBe(0);
        second.Skipped.ShouldBe(1); // only the latest stored close is re-fetched (in case the provider corrected it)
        (await ctx.Db.PriceHistories.CountAsync()).ShouldBe(2);
        (await ctx.Db.CorporateActions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Records_a_failure_when_no_source_supports_the_symbol()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var importer = NewImporter(ctx, new FakePriceHistorySource { Enabled = false });

        var result = await importer.ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        result.Upserted.ShouldBe(0);
        result.Failed.ShouldBe(1);
        (await ctx.Db.PriceHistories.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Tickers_filter_restricts_which_series_are_fetched()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        await SeedHeldSymbolAsync(ctx, "MSFT");

        var source = new FakePriceHistorySource
        {
            Handler = req => new PriceSeriesResult(
                new[] { new PricePoint(new DateOnly(2025, 1, 2), 100m) },
                Array.Empty<SplitEvent>(),
                "USD"),
        };
        var importer = NewImporter(ctx, source);

        await importer.ImportAsync(new PriceHistoryImportOptions(Tickers: new[] { "AAPL" }), CancellationToken.None);

        source.Requests.ShouldHaveSingleItem().Symbol.ShouldBe("AAPL");
        (await ctx.Db.PriceHistories.AllAsync(p => p.SymbolKey == "AAPL")).ShouldBeTrue();
    }

    [Fact]
    public async Task Fetches_from_a_week_before_the_accounts_first_trade_not_the_holdings()
    {
        // A position held before the imported history is valued from the account's start, so its prices are
        // needed from there — even though this holding's own first row is much later.
        await using var ctx = await TestDbContext.CreateAsync();
        var accountId = await SeedHeldSymbolAsync(ctx, "VOO");
        ctx.Db.AccountTransactions.Add(new AccountTransaction(
            accountId, "TEST", Guid.NewGuid().ToString("N"), TransactionType.Deposit, new DateOnly(2024, 3, 1), 500m));
        await ctx.Db.SaveChangesAsync();
        var source = new FakePriceHistorySource();

        await NewImporter(ctx, source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        source.Requests.ShouldHaveSingleItem().From.ShouldBe(new DateOnly(2024, 2, 23));
    }

    [Fact]
    public async Task Backfills_missing_history_before_the_earliest_stored_close()
    {
        // Prices were first fetched from a later date (e.g. before older history was imported): the gap before
        // the earliest stored close is fetched too, not just the tail after the latest.
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        ctx.Db.PriceHistories.Add(PriceHistory.ForSymbol(
            "AAPL", new DateOnly(2025, 3, 3), 120m, "USD", PriceSource.Stooq));
        await ctx.Db.SaveChangesAsync();
        var source = new FakePriceHistorySource
        {
            Handler = _ => new PriceSeriesResult(
                new[] { new PricePoint(new DateOnly(2025, 1, 2), 100m), new PricePoint(new DateOnly(2025, 3, 3), 120m) },
                Array.Empty<SplitEvent>(),
                "USD"),
        };

        var result = await NewImporter(ctx, source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        source.Requests.Select(r => (r.From, r.To < new DateOnly(2025, 3, 3) ? r.To : (DateOnly?)null))
            .First().ShouldBe((Buy.AddDays(-7), (DateOnly?)new DateOnly(2025, 3, 2)));
        result.Upserted.ShouldBe(1); // the 2025-01-02 close
        (await ctx.Db.PriceHistories.CountAsync()).ShouldBe(2);
    }

    private static PriceHistoryImporter NewImporter(TestDbContext ctx, IPriceHistorySource source) =>
        new(ctx.Db,
            new PriceHistorySourceSelector(new[] { source }),
            NullLogger<PriceHistoryImporter>.Instance);

    private static async Task<Guid> SeedHeldSymbolAsync(TestDbContext ctx, string symbol)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "acct", "vanguard.com", $"num-{symbol}");
        ctx.Db.Accounts.Add(account);

        var holding = new AccountHolding(account.AccountId, AccountHoldingKind.Other);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);

        var tx = new AccountTransaction(
            account.AccountId, "TEST", Guid.NewGuid().ToString("N"), TransactionType.Buy, Buy, amount: -100m);
        tx.LinkToHolding(holding.AccountHoldingId);
        ctx.Db.AccountTransactions.Add(tx);

        await ctx.Db.SaveChangesAsync();
        return account.AccountId;
    }
}
