using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Persistence;

public sealed class ReferenceDataSeedTests
{
    [Fact]
    public async Task Currency_seed_includes_iso_4217_codes_with_correct_minor_units()
    {
        await using var ctx = await TestDbContext.CreateAsync();

        var usd = await ctx.Db.Currencies.SingleAsync(c => c.Code == "USD");
        usd.MinorUnit.ShouldBe(2);
        usd.Name.ShouldBe("US Dollar");

        var jpy = await ctx.Db.Currencies.SingleAsync(c => c.Code == "JPY");
        jpy.MinorUnit.ShouldBe(0);

        var bhd = await ctx.Db.Currencies.SingleAsync(c => c.Code == "BHD");
        bhd.MinorUnit.ShouldBe(3);

        var clf = await ctx.Db.Currencies.SingleAsync(c => c.Code == "CLF");
        clf.MinorUnit.ShouldBe(4);
    }

    [Fact]
    public async Task Currency_seed_contains_full_iso_4217_set()
    {
        await using var ctx = await TestDbContext.CreateAsync();

        var count = await ctx.Db.Currencies.CountAsync();

        count.ShouldBeGreaterThan(150);
    }

    [Fact]
    public async Task Country_seed_includes_iso_3166_alpha2_and_kosovo()
    {
        await using var ctx = await TestDbContext.CreateAsync();

        (await ctx.Db.Countries.AnyAsync(c => c.Code == "US")).ShouldBeTrue();
        (await ctx.Db.Countries.AnyAsync(c => c.Code == "GB")).ShouldBeTrue();
        (await ctx.Db.Countries.AnyAsync(c => c.Code == "XK")).ShouldBeTrue();
        (await ctx.Db.Countries.CountAsync()).ShouldBeGreaterThan(240);
    }

    [Fact]
    public async Task AssetCategory_seed_includes_sec_nport_enumeration()
    {
        await using var ctx = await TestDbContext.CreateAsync();

        foreach (var expected in new[] { "EC", "EP", "DBT", "STIV", "RA", "LON", "ABS-MBS", "COMM", "RE" })
            (await ctx.Db.AssetCategories.AnyAsync(c => c.Code == expected)).ShouldBeTrue(expected);
    }

    [Fact]
    public async Task FundHolding_rejects_unknown_currency_code()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, snapshotId) = await SeedFundSnapshotAsync(ctx);

        var holding = new FundHolding(snapshotId, 0.05m);
        holding.SetClassification(null, null, "ZZZ");
        ctx.Db.FundHoldings.Add(holding);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.Db.SaveChangesAsync());
        ex.InnerException!.Message.ShouldContain("FOREIGN KEY");
    }

    [Fact]
    public async Task FundHolding_rejects_unknown_country_code()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, snapshotId) = await SeedFundSnapshotAsync(ctx);

        var holding = new FundHolding(snapshotId, 0.05m);
        holding.SetClassification(null, "ZZ", null);
        ctx.Db.FundHoldings.Add(holding);

        await Should.ThrowAsync<DbUpdateException>(() => ctx.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task FundHolding_accepts_known_codes()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, snapshotId) = await SeedFundSnapshotAsync(ctx);

        var holding = new FundHolding(snapshotId, 0.05m);
        holding.SetClassification("EC", "US", "USD");
        ctx.Db.FundHoldings.Add(holding);

        await ctx.Db.SaveChangesAsync();
    }

    private static async Task<(Guid FundId, Guid SnapshotId)> SeedFundSnapshotAsync(TestDbContext ctx)
    {
        var fund = new Fund("S000000001");
        ctx.Db.Funds.Add(fund);
        var snapshot = new FundSnapshot(fund.FundId, new DateOnly(2026, 6, 30), "filing", "https://example.test/seed");
        ctx.Db.FundSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        return (fund.FundId, snapshot.FundSnapshotId);
    }
}
