using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Pricing;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;
using Vizfolio.Infrastructure.Pricing;
using Vizfolio.Infrastructure.Pricing.Hosted;

namespace Vizfolio.Api.Tests.Pricing;

/// <summary>
/// Prices that just work (roadmap Phase 3): imports queue a background fetch, the queue knows which accounts are
/// waiting on prices, the schedule runs after the US close, and provider keys can be saved from Settings without
/// ever overriding server configuration.
/// </summary>
public sealed class PriceRefreshTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    // ---------------- queue ----------------

    [Fact]
    public void Queued_accounts_are_pending_until_the_run_that_takes_them_finishes()
    {
        var queue = Queue();
        queue.Enqueue(PriceRefreshRequest.ForAccounts([A], PriceRefreshTrigger.Import));

        queue.IsPending(A).ShouldBeTrue();
        queue.IsPending(B).ShouldBeFalse();
        queue.Current.Pending.ShouldBeTrue();

        var run = queue.StartNext().ShouldNotBeNull();
        run.AccountIds.ShouldBe([A]);
        queue.IsPending(A).ShouldBeTrue(); // running
        queue.Current.Running.ShouldBeTrue();

        queue.Finished(new ImportResult(10, 4, 6, 1, [], [], TimeSpan.Zero), error: null);
        queue.IsPending(A).ShouldBeFalse();
        queue.Current.ShouldSatisfyAllConditions(
            s => s.Running.ShouldBeFalse(),
            s => s.LastUpserted.ShouldBe(4),
            s => s.LastFailed.ShouldBe(1),
            s => s.LastTrigger.ShouldBe("Import"));
    }

    [Fact]
    public void Everything_queued_at_once_becomes_one_run_and_a_full_refresh_covers_every_account()
    {
        var queue = Queue();
        queue.Enqueue(PriceRefreshRequest.ForAccounts([A], PriceRefreshTrigger.Import));
        queue.Enqueue(PriceRefreshRequest.ForAccounts([B], PriceRefreshTrigger.Import));

        queue.StartNext()!.AccountIds!.Order().ShouldBe(new[] { A, B }.Order());
        queue.StartNext().ShouldBeNull();

        queue.Enqueue(PriceRefreshRequest.ForAccounts([A], PriceRefreshTrigger.Import));
        queue.Enqueue(PriceRefreshRequest.Everything(PriceRefreshTrigger.Manual));
        queue.IsPending(Guid.NewGuid()).ShouldBeTrue();
        var run = queue.StartNext()!;
        run.AccountIds.ShouldBeNull();
        run.Trigger.ShouldBe(PriceRefreshTrigger.Manual);
    }

    [Fact]
    public void Import_triggered_fetches_can_be_switched_off()
    {
        var queue = Queue(new PriceHistoryOptions { RefreshOnImport = false });

        queue.Enqueue(PriceRefreshRequest.ForAccounts([A], PriceRefreshTrigger.Import));
        queue.Enqueue(PriceRefreshRequest.ForAccounts([B], PriceRefreshTrigger.Manual));

        queue.IsPending(A).ShouldBeFalse();
        queue.IsPending(B).ShouldBeTrue();
    }

    // ---------------- schedule ----------------

    [Theory]
    [InlineData("2026-10-05T15:00:00Z", "2026-10-06T00:00:00Z")] // 11:00 EDT → 20:00 EDT the same day
    [InlineData("2026-10-06T01:00:00Z", "2026-10-07T00:00:00Z")] // 21:00 EDT → tomorrow
    [InlineData("2026-12-01T12:00:00Z", "2026-12-02T01:00:00Z")] // winter: 20:00 EST is 01:00 UTC
    public void The_daily_refresh_runs_at_eight_pm_new_york_time(string now, string expected)
    {
        var schedule = new PriceSchedule();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var next = PriceScheduleClock.NextRun(DateTimeOffset.Parse(now), schedule, zone);

        next.ShouldBe(DateTimeOffset.Parse(expected));
    }

    [Theory]
    [InlineData("2026-10-05T15:00:00Z", "2026-10-05T00:00:00Z")] // 11:00 EDT → last night's 20:00
    [InlineData("2026-10-06T01:00:00Z", "2026-10-06T00:00:00Z")] // 21:00 EDT → tonight's 20:00
    public void The_latest_close_is_the_most_recent_eight_pm_new_york_time(string now, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        PriceScheduleClock.LatestClose(DateTimeOffset.Parse(now), new PriceSchedule(), zone).ShouldBe(DateTimeOffset.Parse(expected));
    }

    [Fact]
    public void Startup_scheduled_and_import_runs_skip_fresh_series_but_a_manual_fetch_skips_nothing()
    {
        var now = DateTimeOffset.Parse("2026-10-05T15:00:00Z");
        var schedule = new PriceSchedule();

        PriceRefreshWorker.OptionsFor(PriceRefreshRequest.Everything(PriceRefreshTrigger.Schedule), now, schedule)
            .FreshSince.ShouldBe(DateTimeOffset.Parse("2026-10-05T00:00:00Z"));
        var import = PriceRefreshWorker.OptionsFor(PriceRefreshRequest.ForAccounts([A], PriceRefreshTrigger.Import), now, schedule);
        import.FreshSince.ShouldNotBeNull();
        import.AccountIds.ShouldBe([A]);
        PriceRefreshWorker.OptionsFor(PriceRefreshRequest.Everything(PriceRefreshTrigger.Manual), now, schedule)
            .FreshSince.ShouldBeNull();
    }

    [Fact]
    public void Without_a_daily_time_the_refresh_runs_every_interval()
    {
        var schedule = new PriceSchedule { DailyAt = null, Interval = TimeSpan.FromHours(6) };
        var now = DateTimeOffset.Parse("2026-10-05T15:00:00Z");

        PriceScheduleClock.NextRun(now, schedule, TimeZoneInfo.Utc).ShouldBe(now.AddHours(6));
    }

    [Fact]
    public void Startup_catches_up_by_default_and_adjusted_stooq_is_off()
    {
        var options = new PriceHistoryOptions();

        options.Schedule.RunOnStartup.ShouldBeTrue();
        options.RefreshOnImport.ShouldBeTrue();
        options.Providers.Stooq.Enabled.ShouldBeFalse();
    }

    // ---------------- provider keys ----------------

    [Fact]
    public async Task A_key_saved_from_settings_is_used_and_remembered_until_cleared()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var store = KeyStore(ctx, new PriceHistoryOptions());

        await store.EnsureLoadedAsync(CancellationToken.None);
        store.GetKeySource(PriceSource.Tiingo).ShouldBe(ProviderKeySource.None);

        await store.SetApiKeyAsync(PriceSource.Tiingo, " saved-key ", CancellationToken.None);
        store.GetApiKey(PriceSource.Tiingo).ShouldBe("saved-key");
        store.GetKeySource(PriceSource.Tiingo).ShouldBe(ProviderKeySource.Settings);

        var restarted = KeyStore(ctx, new PriceHistoryOptions());
        await restarted.EnsureLoadedAsync(CancellationToken.None);
        restarted.GetApiKey(PriceSource.Tiingo).ShouldBe("saved-key");

        await restarted.SetApiKeyAsync(PriceSource.Tiingo, "", CancellationToken.None);
        restarted.GetApiKey(PriceSource.Tiingo).ShouldBeNull();
        ctx.Db.AppSettings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_configured_key_wins_over_a_saved_one()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var options = new PriceHistoryOptions();
        options.Providers.Tiingo.ApiKey = "from-config";
        var store = KeyStore(ctx, options);
        await store.SetApiKeyAsync(PriceSource.Tiingo, "saved-key", CancellationToken.None);

        store.GetApiKey(PriceSource.Tiingo).ShouldBe("from-config");
        store.GetKeySource(PriceSource.Tiingo).ShouldBe(ProviderKeySource.Configuration);
    }

    // ---------------- fetch on import (3.1) ----------------

    [Fact]
    public async Task An_import_queues_a_price_fetch_for_the_accounts_it_touched()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "example.com", "1234");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        var queue = new RecordingQueue();
        var row = new ParsedTransaction(null, TransactionType.Deposit, new DateOnly(2025, 1, 2), null, null, null, null, null, 100m, null, null, null);
        var service = new PortfolioImportService(
            ctx.Db, [new OneRowParser(row)], new NoImplied(), NullLogger<PortfolioImportService>.Instance,
            ledgerRelinker: null, priceRefresh: queue);

        await service.ImportToAccountAsync(account.AccountId, new MemoryStream(Encoding.UTF8.GetBytes("x")), "x.dat", CancellationToken.None, "STUB");

        var request = queue.Requests.ShouldHaveSingleItem();
        request.AccountIds.ShouldBe([account.AccountId]);
        request.Trigger.ShouldBe(PriceRefreshTrigger.Import);
    }

    [Fact]
    public void The_fallback_chain_lists_supporting_providers_highest_priority_first()
    {
        var low = new FakePriceHistorySource { Source = PriceSource.Eodhd, Priority = 10 };
        var off = new FakePriceHistorySource { Source = PriceSource.AlphaVantage, Priority = 20, Enabled = false };
        var high = new FakePriceHistorySource { Source = PriceSource.Tiingo, Priority = 30 };
        var selector = new PriceHistorySourceSelector([low, off, high]);

        selector.SelectAll(new PriceSeriesRequest("AAPL", null, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1)))
            .Select(s => s.Source).ShouldBe([PriceSource.Tiingo, PriceSource.Eodhd]);
        selector.All.Count.ShouldBe(3);
    }

    // ---------------- helpers ----------------

    private static PriceRefreshQueue Queue(PriceHistoryOptions? options = null)
        => new(new StaticOptionsMonitor(options ?? new PriceHistoryOptions()));

    private static ProviderKeyStore KeyStore(TestDbContext ctx, PriceHistoryOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppDbContext>(ctx.Db);
        return new ProviderKeyStore(
            new StaticOptionsMonitor(options), services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProviderKeyStore>.Instance);
    }

    private sealed class StaticOptionsMonitor(PriceHistoryOptions value) : IOptionsMonitor<PriceHistoryOptions>
    {
        public PriceHistoryOptions CurrentValue => value;

        public PriceHistoryOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<PriceHistoryOptions, string?> listener) => null;
    }

    private sealed class RecordingQueue : IPriceRefreshQueue
    {
        public List<PriceRefreshRequest> Requests { get; } = [];

        public void Enqueue(PriceRefreshRequest request) => Requests.Add(request);
    }

    private sealed class NoImplied : IImpliedContributionService
    {
        public Task<ImpliedContributionPreview?> PreviewForAccountAsync(Guid portfolioId, Guid accountId, CancellationToken cancellationToken)
            => Task.FromResult<ImpliedContributionPreview?>(null);

        public Task<ImpliedContributionSyncResult> SyncForAccountAsync(Guid accountId, CancellationToken cancellationToken)
            => Task.FromResult(new ImpliedContributionSyncResult(0, 0m));
    }

    private sealed class OneRowParser(ParsedTransaction row) : IPortfolioFileParser
    {
        public string SourceSystem => "STUB";
        public string DisplayName => "Stub";
        public int Priority => 0;
        public IReadOnlyCollection<string> FileExtensions { get; } = [".dat"];

        public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
            => Task.FromResult(new ParsedPortfolioFile(SourceSystem, [new ParsedAccountStatement(null, null, [row], [], null)]));
    }
}
