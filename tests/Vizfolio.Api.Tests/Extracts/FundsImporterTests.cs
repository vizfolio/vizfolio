using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts.Fakes;
using Vizfolio.Application.Extracts.Importers;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class FundsImporterTests
{
    private const string SeriesId = "S000012345";
    private const string Period = "2024-12-31";
    private static readonly DateOnly AsOf = new(2024, 12, 31);

    [Fact]
    public async Task Imports_new_fund_with_share_classes_returns_and_holdings()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(out var entry);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry, holdings: [SampleHolding("0000320193", 0.05m)]);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        result.Upserted.ShouldBe(1);
        result.Skipped.ShouldBe(0);

        var fund = await ctx.Db.Funds.AsNoTracking().SingleAsync();
        fund.SeriesId.ShouldBe(SeriesId);
        fund.Name.ShouldBe("Sample Fund");

        var snapshot = await ctx.Db.FundSnapshots.AsNoTracking()
            .Include(s => s.ShareClasses).Include(s => s.MonthlyReturns)
            .SingleAsync();
        snapshot.AsOf.ShouldBe(AsOf);
        snapshot.NetAssetsUsd.ShouldBe(1_000_000m);
        snapshot.ShareClasses.Count.ShouldBe(1);
        snapshot.MonthlyReturns.Count.ShouldBe(1);

        var holding = await ctx.Db.FundHoldings.AsNoTracking().SingleAsync();
        holding.Weight.ShouldBe(0.05m);
        holding.IssuerCik.ShouldBe("0000320193");
        holding.SecurityId.ShouldBeNull();
    }

    [Fact]
    public async Task Skips_fund_when_manifest_period_is_not_newer_than_existing_snapshot()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(out var entry);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry);

        await SeedFundWithSnapshot(ctx, SeriesId, AsOf);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        result.Skipped.ShouldBe(1);
        result.Upserted.ShouldBe(0);
        source.FetchedKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Imports_newer_snapshot_for_existing_fund()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedFundWithSnapshot(ctx, SeriesId, new DateOnly(2024, 9, 30));

        var source = BuildSource(out var entry);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        result.Upserted.ShouldBe(1);
        (await ctx.Db.FundSnapshots.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Force_flag_replaces_existing_snapshot_with_same_as_of()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var fundId = await SeedFundWithSnapshot(ctx, SeriesId, AsOf);
        await SeedHolding(ctx, fundId, AsOf, weight: 0.99m);

        var source = BuildSource(out var entry);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry, holdings: [SampleHolding(null, 0.05m)]);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions(Force: true));

        result.Upserted.ShouldBe(1);
        (await ctx.Db.FundSnapshots.CountAsync()).ShouldBe(1);
        var holding = await ctx.Db.FundHoldings.AsNoTracking().SingleAsync();
        holding.Weight.ShouldBe(0.05m);
    }

    [Fact]
    public async Task Filter_restricts_to_requested_series_ids()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = new FakeFundsExtractSource();
        source.ManifestEntries.Add(new FundsManifestEntry(SeriesId, Period, "Sample Fund", "0000123", null, ["FUND"]));
        source.ManifestEntries.Add(new FundsManifestEntry("S000999999", Period, "Other Fund", null, null, null));
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(source.ManifestEntries[0]);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions(SeriesIds: [SeriesId]));

        result.Considered.ShouldBe(1);
        result.Upserted.ShouldBe(1);
        source.FetchedKeys.ShouldBe([$"{SeriesId}/{Period}"]);
    }

    [Fact]
    public async Task Auto_links_holding_to_existing_security_by_issuer_cik()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var security = new Security("0000320193", DateTimeOffset.UtcNow);
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var source = BuildSource(out var entry);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry, holdings: [SampleHolding("320193", 0.10m)]);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        await importer.ImportAsync(new FundsImportOptions());

        var holding = await ctx.Db.FundHoldings.AsNoTracking().SingleAsync();
        holding.SecurityId.ShouldBe(security.SecurityId);
        holding.IssuerCik.ShouldBe("0000320193");
    }

    [Fact]
    public async Task Records_failure_when_latest_period_is_unparseable()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = new FakeFundsExtractSource();
        source.ManifestEntries.Add(new FundsManifestEntry(SeriesId, "not-a-date", "Sample Fund", null, null, null));

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        result.Failed.ShouldBe(1);
        result.Failures.Single().Key.ShouldBe(SeriesId);
    }

    [Fact]
    public async Task Unknown_holding_reference_codes_are_dropped_and_reported()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(out var entry);
        var holding = new HoldingExtract(
            Weight: 0.1m,
            Name: "Mystery Inc.",
            Ticker: "MYST",
            Isin: null,
            IssuerCik: null,
            AssetCategory: "ZZZ",
            Country: "Q1",
            Currency: "ZZZ",
            Balance: null,
            FairValueUsd: null);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry, holdings: [holding]);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        result.Upserted.ShouldBe(1);

        var stored = await ctx.Db.FundHoldings.AsNoTracking().SingleAsync();
        stored.AssetCategoryCode.ShouldBeNull();
        stored.CountryCode.ShouldBeNull();
        stored.CurrencyCode.ShouldBeNull();

        result.DataCleaning.Count.ShouldBe(3);
        result.DataCleaning.ShouldContain(d =>
            d.Field == "Currency" && d.OriginalValue == "ZZZ" && d.Occurrences == 1);
        result.DataCleaning.ShouldContain(d =>
            d.Field == "Country" && d.OriginalValue == "Q1" && d.Occurrences == 1);
        result.DataCleaning.ShouldContain(d =>
            d.Field == "AssetCategory" && d.OriginalValue == "ZZZ" && d.Occurrences == 1);
    }

    [Fact]
    public async Task Known_holding_reference_codes_are_persisted_and_not_reported()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(out var entry);
        source.Snapshots[$"{SeriesId}/{Period}"] = Snapshot(entry, holdings: [SampleHolding(null, 0.05m)]);

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        var stored = await ctx.Db.FundHoldings.AsNoTracking().SingleAsync();
        stored.AssetCategoryCode.ShouldBe("EC");
        stored.CountryCode.ShouldBe("US");
        stored.CurrencyCode.ShouldBe("USD");

        result.DataCleaning.ShouldBeEmpty();
    }

    [Fact]
    public async Task Records_failure_when_snapshot_is_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        BuildSource(out _);
        var source = new FakeFundsExtractSource();
        source.ManifestEntries.Add(new FundsManifestEntry(SeriesId, Period, "Sample Fund", null, null, null));

        var importer = new FundsImporter(ctx.Db, source, NullLogger<FundsImporter>.Instance);
        var result = await importer.ImportAsync(new FundsImportOptions());

        result.Failed.ShouldBe(1);
        result.Failures.Single().Reason.ShouldBe("Fund snapshot not found.");
    }

    private static FakeFundsExtractSource BuildSource(out FundsManifestEntry entry)
    {
        var source = new FakeFundsExtractSource();
        entry = new FundsManifestEntry(SeriesId, Period, "Sample Fund", "0000123", "A-1", ["FUND"]);
        source.ManifestEntries.Add(entry);
        return source;
    }

    private static FundSnapshotExtract Snapshot(FundsManifestEntry entry, IReadOnlyList<HoldingExtract>? holdings = null) =>
        new(
            SchemaVersion: "0.4",
            GeneratedAt: DateTimeOffset.UtcNow,
            Fund: new FundExtract(
                SeriesId: entry.SeriesId,
                AsOf: AsOf,
                SourceFiling: "0000123-24-000001",
                SourceUrl: "https://example.test/source",
                Name: entry.Name,
                RegistrantCik: entry.RegistrantCik,
                RegistrantName: "Sample Registrant",
                NetAssetsUsd: 1_000_000m,
                TotalAssetsUsd: 1_200_000m,
                TotalLiabilitiesUsd: 200_000m,
                CashNotInPortfolioUsd: 50_000m,
                IsFinalFiling: false,
                ShareClasses:
                [
                    new ShareClassExtract("C000111", "Investor", "FUND", 0.0050m)
                ],
                MonthlyReturns:
                [
                    new MonthlyReturnExtract(new DateOnly(2024, 12, 1), 0.0123m, "C000111")
                ]),
            Holdings: holdings ?? []);

    private static HoldingExtract SampleHolding(string? issuerCik, decimal weight) =>
        new(
            Weight: weight,
            Name: "Apple Inc.",
            Ticker: "AAPL",
            Isin: "US0378331005",
            IssuerCik: issuerCik,
            AssetCategory: "EC",
            Country: "US",
            Currency: "USD",
            Balance: 100m,
            FairValueUsd: 12_345.67m);

    private static async Task<Guid> SeedFundWithSnapshot(TestDbContext ctx, string seriesId, DateOnly asOf)
    {
        var fund = new Fund(seriesId);
        ctx.Db.Funds.Add(fund);
        var snapshot = new FundSnapshot(fund.FundId, asOf, "filing", "https://example.test/seed");
        ctx.Db.FundSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        return fund.FundId;
    }

    private static async Task SeedHolding(TestDbContext ctx, Guid fundId, DateOnly asOf, decimal weight)
    {
        var snapshot = await ctx.Db.FundSnapshots
            .SingleAsync(s => s.FundId == fundId && s.AsOf == asOf);
        var holding = new FundHolding(snapshot.FundSnapshotId, weight);
        ctx.Db.FundHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
    }
}
