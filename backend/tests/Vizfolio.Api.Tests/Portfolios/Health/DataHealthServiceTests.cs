using System.Text.Json;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Portfolios.Health;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

using static Vizfolio.Api.Tests.Portfolios.PortfolioSeed;

namespace Vizfolio.Api.Tests.Portfolios.Health;

/// <summary>
/// Data Health (roadmap 6.1): every reason a number is blank, approximate or assumed becomes a plain-language finding
/// with something to do about it. Findings that blank a headline number are Blocking; the rest are Info.
/// </summary>
public sealed class DataHealthServiceTests
{
    private static readonly DateOnly Jan2 = new(2025, 1, 2);
    private static readonly DateOnly Today = new(2025, 2, 1);

    [Fact]
    public async Task A_fully_priced_history_that_matches_its_statement_is_healthy()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedCashAsync(ctx, accountId, Jan2, TransactionType.Deposit, 1000m);
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        await SeedWeeklyPricesAsync(ctx, "ZXFND", 100m);
        await SeedSnapshotAsync(ctx, fund, new DateOnly(2025, 1, 31), marketValue: 1000m, quantity: 10m);

        var report = await NewService(ctx).GetForPortfolioAsync(portfolioId, CancellationToken.None);

        report!.Status.ShouldBe(HealthStatuses.Healthy);
        report.Findings.ShouldBeEmpty();
        report.Accounts.ShouldHaveSingleItem().Status.ShouldBe(HealthStatuses.Healthy);
    }

    [Fact]
    public async Task A_holding_without_prices_is_blocking_for_the_days_it_is_held_and_offers_to_fetch_them()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedCashAsync(ctx, accountId, Jan2, TransactionType.Deposit, 1000m);
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Buy, -1000m, quantity: 10m);

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        report!.Status.ShouldBe(HealthStatuses.NeedsAttention);
        var finding = report.Findings.ShouldHaveSingleItem();
        finding.Code.ShouldBe(HealthCodes.UnpricedHolding);
        finding.Severity.ShouldBe(HealthSeverities.Blocking);
        finding.Symbol.ShouldBe("ZXFND");
        finding.From.ShouldBe(Jan2);
        finding.To.ShouldBe(Today);
        finding.Message.ShouldBe("No price for ZXFND from Jan 2, 2025 to Feb 1, 2025, so the account's value is unknown on those days.");
        finding.Action.Kind.ShouldBe(HealthActionKinds.FetchPrices);
    }

    [Fact]
    public async Task A_holding_no_provider_has_points_to_adding_a_provider()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        var status = PriceSeriesStatus.ForSymbol("ZXFND", "ZXFND");
        status.RecordAttempt(PriceFetchOutcome.NoSource, null, "No provider supports ZXFND.", Jan2, null, null);
        ctx.Db.PriceSeriesStatuses.Add(status);
        await ctx.Db.SaveChangesAsync();

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.Single(f => f.Code == HealthCodes.UnpricedHolding);
        finding.Action.Kind.ShouldBe(HealthActionKinds.AddPriceProviderKey);
        finding.Details.PriceFetchOutcome.ShouldBe("NoSource");
        finding.Message.ShouldEndWith("No configured price provider has it.");
    }

    [Fact]
    public async Task Missing_prices_while_a_fetch_is_running_are_informational()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Buy, -1000m, quantity: 10m);

        var report = await NewService(ctx, pending: true).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.Single(f => f.Code == HealthCodes.UnpricedHolding);
        finding.Severity.ShouldBe(HealthSeverities.Info);
        finding.Details.PricesPending.ShouldBeTrue();
        finding.Message.ShouldStartWith("Prices for ZXFND are still downloading");
    }

    [Fact]
    public async Task A_price_that_goes_stale_is_reported_from_when_it_became_too_old()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        await SeedPriceAsync(ctx, "ZXFND", Jan2, 100m);

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.ShouldHaveSingleItem();
        finding.Code.ShouldBe(HealthCodes.StalePrice);
        finding.From.ShouldBe(Jan2.AddDays(new ValuationOptions().MaxPriceAgeDays + 1));
        finding.To.ShouldBe(Today);
    }

    [Fact]
    public async Task A_material_statement_mismatch_is_blocking_and_a_tiny_one_is_informational()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var big = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXBIG");
        var tiny = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXTNY");
        await SeedTransactionAsync(ctx, accountId, big, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        await SeedTransactionAsync(ctx, accountId, tiny, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        await SeedTransactionAsync(ctx, accountId, big, new DateOnly(2025, 1, 10), TransactionType.Buy, -1000m, quantity: 10m);
        await SeedTransactionAsync(ctx, accountId, tiny, new DateOnly(2025, 1, 10), TransactionType.Buy, -1000m, quantity: 10m);
        await SeedWeeklyPricesAsync(ctx, "ZXBIG", 100m);
        await SeedWeeklyPricesAsync(ctx, "ZXTNY", 100m);
        // The first statement agrees (it anchors the opening); the next one is where the ledger drifts.
        await SeedSnapshotAsync(ctx, big, new DateOnly(2025, 1, 6), marketValue: 1000m, quantity: 10m);
        await SeedSnapshotAsync(ctx, tiny, new DateOnly(2025, 1, 6), marketValue: 1000m, quantity: 10m);
        var statement = new DateOnly(2025, 1, 20);
        await SeedSnapshotAsync(ctx, big, statement, marketValue: 3000m, quantity: 30m);   // 10 shares the ledger doesn't have
        await SeedSnapshotAsync(ctx, tiny, statement, marketValue: 2000.1m, quantity: 20.001m); // a rounding residue

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var material = report!.Findings.Single(f => f.Code == HealthCodes.QuantityMismatch && f.Symbol == "ZXBIG");
        material.Severity.ShouldBe(HealthSeverities.Blocking);
        material.To.ShouldBe(statement);
        material.Details.LedgerQuantity.ShouldBe(20m);
        material.Details.BrokerQuantity.ShouldBe(30m);
        material.Action.Kind.ShouldBe(HealthActionKinds.ReimportFile);
        material.Message.ShouldContain("the transactions add up to 20 shares, the statement says 30");

        var immaterial = report.Findings.Single(f => f.Code == HealthCodes.QuantityMismatch && f.Symbol == "ZXTNY");
        immaterial.Severity.ShouldBe(HealthSeverities.Info);
        immaterial.Action.Kind.ShouldBe(HealthActionKinds.None);
    }

    [Fact]
    public async Task Selling_shares_the_history_never_bought_is_blocking()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Sell, 500m, quantity: -5m);
        await SeedWeeklyPricesAsync(ctx, "ZXFND", 100m);

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.Single(f => f.Code == HealthCodes.NegativePosition);
        finding.Severity.ShouldBe(HealthSeverities.Blocking);
        finding.From.ShouldBe(Jan2);
        report.Findings.ShouldNotContain(f => f.Code == HealthCodes.UnpricedHolding); // the negative position says it
    }

    [Fact]
    public async Task A_position_held_before_the_imported_history_is_explained_with_a_way_to_adjust_it()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        await SeedWeeklyPricesAsync(ctx, "ZXFND", 100m);
        await SeedSnapshotAsync(ctx, fund, new DateOnly(2025, 1, 31), marketValue: 1500m, quantity: 15m);

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        report!.Status.ShouldBe(HealthStatuses.Info);
        var finding = report.Findings.Single(f => f.Code == HealthCodes.PreHistoryPosition);
        finding.Severity.ShouldBe(HealthSeverities.Info);
        finding.Message.ShouldBe("We assumed you held 5 ZXFND before your first imported transaction, from your broker's statement.");
        finding.Action.Kind.ShouldBe(HealthActionKinds.AdjustStartingPosition);
    }

    [Fact]
    public async Task A_transfer_of_shares_with_no_price_is_blocking()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var fund = await SeedHoldingWithSymbolAsync(ctx, accountId, "ZXFND");
        await SeedTransactionAsync(ctx, accountId, fund, Jan2, TransactionType.Transfer, 0m, quantity: 12m);

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.Single(f => f.Code == HealthCodes.UnvaluedTransfer);
        finding.Severity.ShouldBe(HealthSeverities.Blocking);
        finding.From.ShouldBe(Jan2);
        finding.Message.ShouldStartWith("A transfer of 12 ");
    }

    [Fact]
    public async Task Implied_contributions_are_summarised_with_a_link_to_review_them()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        foreach (var (date, amount) in new[] { (new DateOnly(2012, 3, 1), 500m), (new DateOnly(2014, 6, 2), 250m) })
        {
            ctx.Db.AccountTransactions.Add(new AccountTransaction(
                accountId, ImpliedContributionService.SourceSystem, $"implied-{date:yyyy-MM-dd}", TransactionType.Deposit, date, amount));
        }
        await ctx.Db.SaveChangesAsync();

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.Single(f => f.Code == HealthCodes.ImpliedContributions);
        finding.Severity.ShouldBe(HealthSeverities.Info);
        finding.Details.Count.ShouldBe(2);
        finding.Details.Amount.ShouldBe(750m);
        finding.Message.ShouldStartWith("2 purchases had no recorded deposit (2012–2014), so $750.00 of contributions were added");
        finding.Action.Kind.ShouldBe(HealthActionKinds.ReviewImpliedContributions);
    }

    [Fact]
    public async Task Rows_the_parser_did_not_understand_are_listed_per_warning_with_examples_and_files()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        await SeedBatchAsync(ctx, portfolioId, accountId, "one.xlsx",
            new ImportWarning(ImportWarningCodes.UnmappedLabel, "Some labels aren't recognised; those rows move nothing.", 2, ["Mystery"]),
            new ImportWarning(ImportWarningCodes.OtherAccountSkipped, "Other accounts were skipped.", 1, ["…2222"]));
        await SeedBatchAsync(ctx, portfolioId, accountId, "two.xlsx",
            new ImportWarning(ImportWarningCodes.UnmappedLabel, "Some labels aren't recognised; those rows move nothing.", 1, ["Oddity"]));

        var report = await NewService(ctx).GetForAccountAsync(portfolioId, accountId, CancellationToken.None);

        var finding = report!.Findings.ShouldHaveSingleItem();
        finding.Code.ShouldBe(HealthCodes.ImportWarning);
        finding.Details.WarningCode.ShouldBe(ImportWarningCodes.UnmappedLabel);
        finding.Details.Count.ShouldBe(3);
        finding.Details.Samples.ShouldBe(["Mystery", "Oddity"]);
        finding.Details.Files.ShouldBe(["one.xlsx", "two.xlsx"], ignoreOrder: true);
        finding.Message.ShouldEndWith("(3 rows)");
    }

    [Fact]
    public async Task The_portfolio_report_lists_blocking_findings_first_and_rates_each_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolioId = await SeedPortfolioAsync(ctx);
        var healthy = await SeedAccountAsync(ctx, portfolioId, "1111");
        var broken = await SeedAccountAsync(ctx, portfolioId, "2222");
        var priced = await SeedHoldingWithSymbolAsync(ctx, healthy, "ZXOK");
        var unpriced = await SeedHoldingWithSymbolAsync(ctx, broken, "ZXNOP");
        await SeedTransactionAsync(ctx, healthy, priced, Jan2, TransactionType.Buy, -1000m, quantity: 10m);
        await SeedWeeklyPricesAsync(ctx, "ZXOK", 100m);
        await SeedSnapshotAsync(ctx, priced, new DateOnly(2025, 1, 31), marketValue: 1500m, quantity: 15m);
        await SeedTransactionAsync(ctx, broken, unpriced, Jan2, TransactionType.Buy, -1000m, quantity: 10m);

        var report = await NewService(ctx).GetForPortfolioAsync(portfolioId, CancellationToken.None);

        report!.Status.ShouldBe(HealthStatuses.NeedsAttention);
        report.Findings[0].Severity.ShouldBe(HealthSeverities.Blocking);
        report.Findings[0].AccountId.ShouldBe(broken);
        report.Accounts.Single(a => a.AccountId == healthy).Status.ShouldBe(HealthStatuses.Info);
        var brokenHealth = report.Accounts.Single(a => a.AccountId == broken);
        brokenHealth.Status.ShouldBe(HealthStatuses.NeedsAttention);
        brokenHealth.Blocking.ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_portfolios_and_accounts_outside_the_portfolio_have_no_report()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var service = NewService(ctx);

        (await service.GetForPortfolioAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await service.GetForAccountAsync(Guid.NewGuid(), accountId, CancellationToken.None)).ShouldBeNull();
        (await service.GetForAccountAsync(portfolioId, accountId, CancellationToken.None))!.Status.ShouldBe(HealthStatuses.Healthy);
    }

    // ---------------- helpers ----------------

    private static async Task SeedWeeklyPricesAsync(TestDbContext ctx, string symbol, decimal close)
    {
        for (var date = new DateOnly(2024, 12, 30); date <= Today; date = date.AddDays(7))
            ctx.Db.PriceHistories.Add(PriceHistory.ForSymbol(symbol, date, close, "USD", PriceSource.Tiingo));
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedBatchAsync(
        TestDbContext ctx, Guid portfolioId, Guid accountId, string fileName, params ImportWarning[] warnings)
    {
        var batch = new ImportBatch(portfolioId, accountId, fileName, Guid.NewGuid().ToString("N"), "VANGUARD", null, null);
        var account = new AccountImportResult(accountId, false, "vanguard.com", "1111", 1, 1, 0, 0, []);
        batch.RecordSummary(JsonSerializer.Serialize(
            new ImportBatchSummary("VANGUARD", [account], warnings), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        ctx.Db.ImportBatches.Add(batch);
        await ctx.Db.SaveChangesAsync();
    }

    private static DataHealthService NewService(TestDbContext ctx, bool pending = false) =>
        new(ctx.Db,
            new AccountValuationLoader(ctx.Db, new ValuationOptions()),
            new PendingStatus(pending),
            new FixedTime(new DateTimeOffset(Today.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero)));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PendingStatus(bool pending) : IPriceRefreshStatus
    {
        public bool IsPending(Guid accountId) => pending;

        public PriceRefreshState Current => new(pending, pending, null, null, null, null, null, null);
    }
}
