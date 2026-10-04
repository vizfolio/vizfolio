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

    // ---------------- fallback chain and outcomes (roadmap Phase 3.3/3.4) ----------------

    [Fact]
    public async Task Falls_back_down_the_providers_when_one_fails_or_has_no_data_and_records_which_answered()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var failing = new FakePriceHistorySource { Source = PriceSource.Tiingo, Priority = 30, Throws = new HttpRequestException("rate limited") };
        var empty = new FakePriceHistorySource { Source = PriceSource.AlphaVantage, Priority = 20 };
        var answering = new FakePriceHistorySource { Source = PriceSource.Eodhd, Priority = 10, Handler = _ => Closes((new(2025, 1, 2), 100m)) };

        var result = await NewImporter(ctx, failing, empty, answering).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        result.Upserted.ShouldBe(1);
        result.Failed.ShouldBe(0);
        (failing.Requests.Count, empty.Requests.Count, answering.Requests.Count).ShouldBe((1, 1, 1));
        (await ctx.Db.PriceHistories.SingleAsync()).Source.ShouldBe(PriceSource.Eodhd);
        var status = await ctx.Db.PriceSeriesStatuses.SingleAsync();
        status.LastOutcome.ShouldBe(PriceFetchOutcome.Ok);
        status.LastSource.ShouldBe(PriceSource.Eodhd);
        status.QuerySymbol.ShouldBe("AAPL");
        status.FirstStored.ShouldBe(new DateOnly(2025, 1, 2));
        status.NeededFrom.ShouldBe(Buy.AddDays(-7));
    }

    [Fact]
    public async Task A_symbol_no_provider_has_data_for_is_reported_as_empty_not_silently_skipped()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "ZZZZ");

        var result = await NewImporter(ctx, new FakePriceHistorySource()).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        result.Failures.ShouldHaveSingleItem().Key.ShouldBe("ZZZZ");
        var status = await ctx.Db.PriceSeriesStatuses.SingleAsync();
        status.LastOutcome.ShouldBe(PriceFetchOutcome.Empty);
        status.Message.ShouldNotBeNull().ShouldContain("No data from");
    }

    [Fact]
    public async Task Every_provider_failing_is_reported_as_failed()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");

        await NewImporter(ctx, new FakePriceHistorySource { Throws = new HttpRequestException("down") })
            .ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        var status = await ctx.Db.PriceSeriesStatuses.SingleAsync();
        status.LastOutcome.ShouldBe(PriceFetchOutcome.Failed);
        status.Message.ShouldNotBeNull().ShouldContain("down");
    }

    [Fact]
    public async Task No_configured_provider_is_reported_as_no_source()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");

        await NewImporter(ctx, new FakePriceHistorySource { Enabled = false }).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        (await ctx.Db.PriceSeriesStatuses.SingleAsync()).LastOutcome.ShouldBe(PriceFetchOutcome.NoSource);
    }

    // ---------------- adjusted closes (3.5) ----------------

    [Fact]
    public async Task Adjusted_closes_are_stored_marked_and_never_stand_in_for_raw_ones()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var adjustedOnly = new FakePriceHistorySource
        {
            Source = PriceSource.Stooq,
            Handler = _ => Closes((new(2025, 1, 2), 99m)) with { Adjusted = true },
        };

        await NewImporter(ctx, adjustedOnly).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        (await ctx.Db.PriceHistories.SingleAsync()).Adjusted.ShouldBeTrue();
        (await ctx.Db.PriceSeriesStatuses.SingleAsync()).LastOutcome.ShouldBe(PriceFetchOutcome.AdjustedOnly);

        // A raw provider added later still fetches the whole window: adjusted rows don't count as coverage.
        var raw = new FakePriceHistorySource { Source = PriceSource.Tiingo, Priority = 30, Handler = _ => Closes((new(2025, 1, 2), 100m)) };
        await NewImporter(ctx, raw, adjustedOnly).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        raw.Requests.ShouldHaveSingleItem().From.ShouldBe(Buy.AddDays(-7));
        (await ctx.Db.PriceHistories.CountAsync(p => !p.Adjusted && p.Close == 100m)).ShouldBe(1);
        (await ctx.Db.PriceSeriesStatuses.SingleAsync()).LastOutcome.ShouldBe(PriceFetchOutcome.Ok);
    }

    [Fact]
    public async Task A_raw_provider_is_preferred_over_an_adjusted_one_higher_in_the_chain()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var adjusted = new FakePriceHistorySource { Priority = 50, Handler = _ => Closes((new(2025, 1, 2), 99m)) with { Adjusted = true } };
        var raw = new FakePriceHistorySource { Source = PriceSource.Tiingo, Priority = 30, Handler = _ => Closes((new(2025, 1, 2), 100m)) };

        await NewImporter(ctx, adjusted, raw).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        (await ctx.Db.PriceHistories.SingleAsync()).Close.ShouldBe(100m);
    }

    // ---------------- ranges (3.2) ----------------

    [Fact]
    public async Task Once_no_provider_has_older_closes_the_backfill_isnt_asked_for_again()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "NEWF");
        var launch = new DateOnly(2025, 3, 3);
        ctx.Db.PriceHistories.Add(PriceHistory.ForSymbol("NEWF", launch, 10m, "USD", PriceSource.Tiingo));
        await ctx.Db.SaveChangesAsync();
        // The fund's history starts at launch: nothing before it.
        var source = new FakePriceHistorySource { Source = PriceSource.Tiingo, Handler = _ => Closes((launch, 10m)) };
        var importer = NewImporter(ctx, source);

        await importer.ImportAsync(new PriceHistoryImportOptions(To: new DateOnly(2025, 3, 5)), CancellationToken.None);
        var status = await ctx.Db.PriceSeriesStatuses.SingleAsync();
        status.NoDataBefore.ShouldBe(launch);
        status.LastOutcome.ShouldBe(PriceFetchOutcome.Ok);
        status.Message.ShouldNotBeNull().ShouldContain("Prices start 2025-03-03");

        source.Requests.Clear();
        await importer.ImportAsync(new PriceHistoryImportOptions(To: new DateOnly(2025, 3, 5)), CancellationToken.None);
        source.Requests.ShouldHaveSingleItem().From.ShouldBe(launch); // only the tail
    }

    [Fact]
    public async Task The_accounts_cash_holding_has_no_price_series()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var accountId = await SeedHeldSymbolAsync(ctx, "AAPL");
        var cash = new AccountHolding(accountId, AccountHoldingKind.Cash);
        cash.SetIdentifiers("$CASH", "Cash", null, null);
        ctx.Db.AccountHoldings.Add(cash);
        await ctx.Db.SaveChangesAsync();
        var source = new FakePriceHistorySource();

        await NewImporter(ctx, source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        source.Requests.ShouldAllBe(r => r.Symbol == "AAPL");
    }

    [Fact]
    public async Task Limiting_to_accounts_fetches_only_their_series_but_over_every_holders_window()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var first = await SeedHeldSymbolAsync(ctx, "AAPL");
        var second = await SeedHeldSymbolAsync(ctx, "MSFT", firstTrade: new DateOnly(2025, 6, 2));
        // An older account also holds MSFT.
        await SeedHeldSymbolAsync(ctx, "MSFT", firstTrade: new DateOnly(2020, 1, 6), accountNumber: "older");
        var source = new FakePriceHistorySource();

        await NewImporter(ctx, source).ImportAsync(new PriceHistoryImportOptions(AccountIds: [second]), CancellationToken.None);

        var request = source.Requests.ShouldHaveSingleItem();
        request.Symbol.ShouldBe("MSFT");
        request.From.ShouldBe(new DateOnly(2020, 1, 6).AddDays(-7));
        first.ShouldNotBe(second);
    }

    // ---------------- skipping series already fetched (no wasted provider requests) ----------------

    private static readonly DateTimeOffset LatestClose = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero); // 8pm New York, Oct 1
    private static readonly DateTimeOffset Now = LatestClose.AddHours(14);

    /// <summary>Closes from the first day the series is needed, so only freshness decides whether to fetch.</summary>
    private static PriceSeriesResult Covering() => Closes((Buy.AddDays(-7), 99m), (new(2025, 1, 2), 100m));

    [Fact]
    public async Task A_series_fetched_since_the_latest_close_isnt_fetched_again()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var source = new FakePriceHistorySource { Handler = _ => Covering() };

        await NewImporter(ctx, Now.AddMinutes(-2), source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);
        source.Requests.Count.ShouldBe(1);

        // Restarting two minutes later: nothing newer can exist, so no request.
        await NewImporter(ctx, Now, source).ImportAsync(new PriceHistoryImportOptions(FreshSince: LatestClose), CancellationToken.None);
        source.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_series_last_fetched_before_the_latest_close_is_fetched_for_the_new_close()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var source = new FakePriceHistorySource { Handler = _ => Covering() };

        await NewImporter(ctx, LatestClose.AddHours(-3), source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);
        await NewImporter(ctx, Now, source).ImportAsync(new PriceHistoryImportOptions(FreshSince: LatestClose), CancellationToken.None);

        source.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Importing_older_history_fetches_the_series_again_even_when_it_was_just_fetched()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var source = new FakePriceHistorySource { Handler = _ => Covering() };
        await NewImporter(ctx, Now.AddMinutes(-2), source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        // An older account holding the same symbol now needs history from 2020.
        await SeedHeldSymbolAsync(ctx, "AAPL", firstTrade: new DateOnly(2020, 1, 6), accountNumber: "older");
        await NewImporter(ctx, Now, source).ImportAsync(new PriceHistoryImportOptions(FreshSince: LatestClose), CancellationToken.None);

        source.Requests.Count.ShouldBeGreaterThan(1);
        source.Requests.ShouldContain(r => r.From < new DateOnly(2025, 1, 1) && r != source.Requests[0]);
    }

    [Fact]
    public async Task A_series_with_no_provider_is_tried_again_so_a_newly_added_key_is_used_at_once()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        await NewImporter(ctx, Now.AddMinutes(-2), new FakePriceHistorySource { Enabled = false })
            .ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        var keyed = new FakePriceHistorySource { Handler = _ => Covering() };
        await NewImporter(ctx, Now, keyed).ImportAsync(new PriceHistoryImportOptions(FreshSince: LatestClose), CancellationToken.None);

        keyed.Requests.Count.ShouldBe(1);
        (await ctx.Db.PriceSeriesStatuses.SingleAsync()).LastOutcome.ShouldBe(PriceFetchOutcome.Ok);
    }

    [Fact]
    public async Task A_failed_series_is_retried_only_once_an_hour_has_passed()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var failing = new FakePriceHistorySource { Throws = new HttpRequestException("429 Too Many Requests") };
        await NewImporter(ctx, Now, failing).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        await NewImporter(ctx, Now.AddMinutes(30), failing).ImportAsync(new PriceHistoryImportOptions(FreshSince: LatestClose), CancellationToken.None);
        failing.Requests.Count.ShouldBe(1);

        await NewImporter(ctx, Now.AddMinutes(61), failing).ImportAsync(new PriceHistoryImportOptions(FreshSince: LatestClose), CancellationToken.None);
        failing.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_run_without_a_freshness_cutoff_or_a_forced_one_skips_nothing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedHeldSymbolAsync(ctx, "AAPL");
        var source = new FakePriceHistorySource { Handler = _ => Covering() };
        await NewImporter(ctx, Now.AddMinutes(-2), source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);

        await NewImporter(ctx, Now, source).ImportAsync(new PriceHistoryImportOptions(), CancellationToken.None);
        await NewImporter(ctx, Now, source).ImportAsync(new PriceHistoryImportOptions(Force: true, FreshSince: LatestClose), CancellationToken.None);

        source.Requests.Count.ShouldBe(3);
    }

    private static PriceHistoryImporter NewImporter(TestDbContext ctx, DateTimeOffset now, params IPriceHistorySource[] sources) =>
        new(ctx.Db,
            new PriceHistorySourceSelector(sources),
            NullLogger<PriceHistoryImporter>.Instance,
            time: new FixedTime(now));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PriceSeriesResult Closes(params (DateOnly Date, decimal Close)[] closes) =>
        new(closes.Select(c => new PricePoint(c.Date, c.Close)).ToList(), Array.Empty<SplitEvent>(), "USD");

    private static PriceHistoryImporter NewImporter(TestDbContext ctx, params IPriceHistorySource[] sources) =>
        new(ctx.Db,
            new PriceHistorySourceSelector(sources),
            NullLogger<PriceHistoryImporter>.Instance);

    private static async Task<Guid> SeedHeldSymbolAsync(
        TestDbContext ctx, string symbol, DateOnly? firstTrade = null, string? accountNumber = null)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "acct", "vanguard.com", accountNumber ?? $"num-{symbol}");
        ctx.Db.Accounts.Add(account);

        var holding = new AccountHolding(account.AccountId, AccountHoldingKind.Other);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);

        var tx = new AccountTransaction(
            account.AccountId, "TEST", Guid.NewGuid().ToString("N"), TransactionType.Buy, firstTrade ?? Buy, amount: -100m);
        tx.LinkToHolding(holding.AccountHoldingId);
        ctx.Db.AccountTransactions.Add(tx);

        await ctx.Db.SaveChangesAsync();
        return account.AccountId;
    }
}
