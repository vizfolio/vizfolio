using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts.Fakes;
using Vizfolio.Application.Extracts.Importers;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class MoneyMarketFundsImporterTests
{
    [Fact]
    public async Task Imports_each_fund_with_its_tickers_and_stable_price()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = new FakeFundsExtractSource
        {
            MoneyMarketRegistry = Registry(
                Fund("S000004462", "0001410368-26-091076", seeks: true, price: 1.0m, "VMFXX", null),
                Fund("S000099999", "0000000000-26-000002", seeks: false, price: null, "FLOAT")),
        };

        var result = await NewImporter(ctx, source).ImportAsync();

        result.Upserted.ShouldBe(2);
        var stable = await ctx.Db.MoneyMarketFunds.AsNoTracking().SingleAsync(f => f.SeriesId == "S000004462");
        stable.Tickers.ShouldBe(new[] { "VMFXX" }); // classes without a ticker are dropped
        stable.StablePrice.ShouldBe(1.0m);
        var floating = await ctx.Db.MoneyMarketFunds.AsNoTracking().SingleAsync(f => f.SeriesId == "S000099999");
        floating.StablePrice.ShouldBeNull();
    }

    [Fact]
    public async Task Re_importing_the_same_filing_skips_it_and_a_newer_filing_updates_it()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = new FakeFundsExtractSource
        {
            MoneyMarketRegistry = Registry(Fund("S000004462", "0001410368-26-000001", seeks: true, price: 1.0m, "VMFXX")),
        };
        var importer = NewImporter(ctx, source);
        await importer.ImportAsync();

        (await importer.ImportAsync()).Skipped.ShouldBe(1);

        source.MoneyMarketRegistry = Registry(Fund("S000004462", "0001410368-26-000002", seeks: true, price: 1.0m, "VMFXX", "VMFAX"));
        (await importer.ImportAsync()).Upserted.ShouldBe(1);
        (await ctx.Db.MoneyMarketFunds.AsNoTracking().SingleAsync()).Tickers.ShouldBe(new[] { "VMFXX", "VMFAX" });
    }

    [Fact]
    public async Task A_registry_that_isnt_published_yet_is_a_no_op()
    {
        await using var ctx = await TestDbContext.CreateAsync();

        var result = await NewImporter(ctx, new FakeFundsExtractSource()).ImportAsync();

        result.Considered.ShouldBe(0);
        (await ctx.Db.MoneyMarketFunds.AnyAsync()).ShouldBeFalse();
    }

    private static MoneyMarketFundsImporter NewImporter(TestDbContext ctx, FakeFundsExtractSource source)
        => new(ctx.Db, source, NullLogger<MoneyMarketFundsImporter>.Instance);

    private static MoneyMarketRegistryExtract Registry(params MoneyMarketFundExtract[] funds)
        => new("1", DateTimeOffset.UtcNow, funds);

    private static MoneyMarketFundExtract Fund(string seriesId, string filing, bool seeks, decimal? price, params string?[] tickers)
        => new(seriesId, $"Fund {seriesId}", "0000106830", new DateOnly(2026, 8, 31), filing, "Government", seeks, price, false,
            tickers.Select((t, i) => new MoneyMarketClassExtract($"C{i:000000000}", t)).ToList());
}
