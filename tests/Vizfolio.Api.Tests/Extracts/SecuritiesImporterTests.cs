using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts.Fakes;
using Vizfolio.Application.Extracts.Importers;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class SecuritiesImporterTests
{
    private static readonly DateTimeOffset BaseFetchedAt = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Inserts_new_security_with_normalized_cik_and_metadata()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(("AAPL", "320193"));
        source.Securities["0000320193"] = Sample("0000320193", "Apple Inc.", BaseFetchedAt, ["AAPL"], ["NASDAQ"]);

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions());

        result.Considered.ShouldBe(1);
        result.Upserted.ShouldBe(1);
        result.Skipped.ShouldBe(0);
        result.Failed.ShouldBe(0);

        var stored = await ctx.Db.Securities.AsNoTracking().SingleAsync();
        stored.Cik.ShouldBe("0000320193");
        stored.Name.ShouldBe("Apple Inc.");
        stored.Sector.ShouldBe("Tech");
        stored.Industry.ShouldBe("Prepackaged Software");
        stored.Tickers.ShouldContain("AAPL");
        stored.Exchanges.ShouldContain("NASDAQ");
        stored.EdgarFetchedAt.ShouldBe(BaseFetchedAt);
    }

    [Fact]
    public async Task Skips_existing_security_when_extract_is_not_newer_and_force_is_false()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedSecurity(ctx, "0000320193", BaseFetchedAt);

        var source = BuildSource(("AAPL", "320193"));
        source.Securities["0000320193"] = Sample("0000320193", "Apple Inc.", BaseFetchedAt);

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions());

        result.Upserted.ShouldBe(0);
        result.Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task Updates_existing_security_when_extract_is_newer()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedSecurity(ctx, "0000320193", BaseFetchedAt);
        var newer = BaseFetchedAt.AddDays(1);

        var source = BuildSource(("AAPL", "320193"));
        source.Securities["0000320193"] = Sample("0000320193", "Apple Inc. Renamed", newer, ["AAPL"], ["NASDAQ"]);

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions());

        result.Upserted.ShouldBe(1);
        var stored = await ctx.Db.Securities.AsNoTracking().SingleAsync();
        stored.Name.ShouldBe("Apple Inc. Renamed");
        stored.EdgarFetchedAt.ShouldBe(newer);
    }

    [Fact]
    public async Task Force_flag_upserts_even_when_extract_is_not_newer()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        await SeedSecurity(ctx, "0000320193", BaseFetchedAt);

        var source = BuildSource(("AAPL", "320193"));
        source.Securities["0000320193"] = Sample("0000320193", "Apple Inc. Renamed", BaseFetchedAt);

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions(Force: true));

        result.Upserted.ShouldBe(1);
        result.Skipped.ShouldBe(0);
        var stored = await ctx.Db.Securities.AsNoTracking().SingleAsync();
        stored.Name.ShouldBe("Apple Inc. Renamed");
    }

    [Fact]
    public async Task Filter_restricts_imports_to_provided_tickers()
    {
        await using var ctx = await TestDbContext.CreateAsync();

        var source = BuildSource(("AAPL", "320193"), ("MSFT", "789019"));
        source.Securities["0000320193"] = Sample("0000320193", "Apple", BaseFetchedAt);
        source.Securities["0000789019"] = Sample("0000789019", "Microsoft", BaseFetchedAt);

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions(Tickers: ["msft"]));

        result.Considered.ShouldBe(1);
        result.Upserted.ShouldBe(1);
        source.FetchedCiks.ShouldBe(["0000789019"]);
    }

    [Fact]
    public async Task Unknown_country_code_is_dropped_and_reported()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(("AAPL", "320193"));
        source.Securities["0000320193"] = Sample("0000320193", "Apple Inc.", BaseFetchedAt) with { Country = "Q1" };

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions());

        result.Upserted.ShouldBe(1);
        var stored = await ctx.Db.Securities.AsNoTracking().SingleAsync();
        stored.CountryCode.ShouldBeNull();

        var entry = result.DataCleaning.ShouldHaveSingleItem();
        entry.Field.ShouldBe("Country");
        entry.OriginalValue.ShouldBe("Q1");
        entry.Occurrences.ShouldBe(1);
    }

    [Fact]
    public async Task Records_failure_when_extract_is_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(("AAPL", "320193"));

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions());

        result.Failed.ShouldBe(1);
        result.Upserted.ShouldBe(0);
        result.Failures.Single().Key.ShouldBe("0000320193");
    }

    [Fact]
    public async Task Records_failure_when_source_throws_for_one_entry_but_continues_with_others()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var source = BuildSource(("AAPL", "320193"), ("MSFT", "789019"));
        source.Securities["0000789019"] = Sample("0000789019", "Microsoft", BaseFetchedAt);
        source.FailFor = cik => cik == "0000320193" ? new InvalidOperationException("boom") : null;

        var importer = new SecuritiesImporter(ctx.Db, source, NullLogger<SecuritiesImporter>.Instance);

        var result = await importer.ImportAsync(new SecuritiesImportOptions());

        result.Failed.ShouldBe(1);
        result.Upserted.ShouldBe(1);
        result.Failures.Single().Reason.ShouldBe("boom");
        (await ctx.Db.Securities.CountAsync()).ShouldBe(1);
    }

    private static FakeSecuritiesExtractSource BuildSource(params (string Ticker, string Cik)[] entries)
    {
        var source = new FakeSecuritiesExtractSource();
        foreach (var (ticker, cik) in entries)
            source.Manifest[ticker] = cik;
        return source;
    }

    private static SecurityExtract Sample(
        string cik,
        string name,
        DateTimeOffset fetchedAt,
        IReadOnlyList<string>? tickers = null,
        IReadOnlyList<string>? exchanges = null) =>
        new(
            Cik: cik,
            Name: name,
            EntityType: "operating",
            Country: "US",
            Sector: "Tech",
            StateOfIncorporation: "DE",
            Sic: "7372",
            SicDescription: "Prepackaged Software",
            Tickers: tickers ?? Array.Empty<string>(),
            Exchanges: exchanges ?? Array.Empty<string>(),
            SchemaVersion: "0.1",
            Source: new SecurityExtractSource(fetchedAt));

    private static async Task SeedSecurity(TestDbContext ctx, string cik, DateTimeOffset fetchedAt)
    {
        var security = new Security(cik, fetchedAt);
        security.UpdateProfile("Existing", null, null, null, "US");
        ctx.Db.Securities.Add(security);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
    }
}
