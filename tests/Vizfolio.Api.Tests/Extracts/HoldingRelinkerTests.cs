using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Application.Extracts.Importers;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class HoldingRelinkerTests
{
    [Fact]
    public async Task Links_previously_unlinked_holdings_when_security_now_exists()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var snapshot = await SeedSnapshot(ctx);
        var holding = new FundHolding(snapshot.FundSnapshotId, 0.10m);
        holding.SetIssuerCik("320193");
        ctx.Db.FundHoldings.Add(holding);
        var security = new Security("0000320193", DateTimeOffset.UtcNow);
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var relinker = new HoldingRelinker(ctx.Db, NullLogger<HoldingRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(1);
        var stored = await ctx.Db.FundHoldings.AsNoTracking().SingleAsync();
        stored.SecurityId.ShouldBe(security.SecurityId);
    }

    [Fact]
    public async Task Leaves_holding_unlinked_when_no_security_matches_issuer_cik()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var snapshot = await SeedSnapshot(ctx);
        var holding = new FundHolding(snapshot.FundSnapshotId, 0.10m);
        holding.SetIssuerCik("999999");
        ctx.Db.FundHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var relinker = new HoldingRelinker(ctx.Db, NullLogger<HoldingRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(0);
        (await ctx.Db.FundHoldings.AsNoTracking().SingleAsync()).SecurityId.ShouldBeNull();
    }

    [Fact]
    public async Task Ignores_holdings_already_linked_to_a_security()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var snapshot = await SeedSnapshot(ctx);
        var security = new Security("0000320193", DateTimeOffset.UtcNow);
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();

        var holding = new FundHolding(snapshot.FundSnapshotId, 0.10m);
        holding.SetIssuerCik("320193");
        holding.LinkToSecurity(security.SecurityId);
        ctx.Db.FundHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var relinker = new HoldingRelinker(ctx.Db, NullLogger<HoldingRelinker>.Instance);
        var linked = await relinker.RelinkAsync();

        linked.ShouldBe(0);
    }

    [Fact]
    public async Task Is_idempotent_across_multiple_runs()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var snapshot = await SeedSnapshot(ctx);
        var security = new Security("0000320193", DateTimeOffset.UtcNow);
        ctx.Db.Securities.Add(security);
        var holding = new FundHolding(snapshot.FundSnapshotId, 0.10m);
        holding.SetIssuerCik("320193");
        ctx.Db.FundHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var relinker = new HoldingRelinker(ctx.Db, NullLogger<HoldingRelinker>.Instance);
        (await relinker.RelinkAsync()).ShouldBe(1);
        (await relinker.RelinkAsync()).ShouldBe(0);
    }

    private static async Task<FundSnapshot> SeedSnapshot(TestDbContext ctx)
    {
        var fund = new Fund("S000XXXXX");
        ctx.Db.Funds.Add(fund);
        var snapshot = new FundSnapshot(fund.FundId, new DateOnly(2024, 12, 31), "filing", "https://example.test/seed");
        ctx.Db.FundSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
        return snapshot;
    }
}
