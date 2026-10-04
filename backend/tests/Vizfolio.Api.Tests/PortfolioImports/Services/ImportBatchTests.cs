using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.PortfolioImports.Fakes;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Services;

/// <summary>
/// Import provenance and fidelity (roadmap Phase 2.1, 2.1b, 2.3, 2.5): every upload is a recorded batch holding its
/// file; what it inserted and created is tagged with it; re-imports and reprocessing bring stored rows in line with
/// the parser (recording what changed); a QFX can go into a single account; statements for one account share one
/// import; statement cash anchors the account's cash.
/// </summary>
public sealed class ImportBatchTests
{
    // ---------------- provenance ----------------

    [Fact]
    public async Task An_import_is_recorded_with_its_file_and_tags_what_it_inserted_and_created()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var qfx = Qfx(Statement("ACCT-1", Buy("B-1", "20260105", "XYZ", 2, -200m), positions: Position("XYZ", 2, 210m)));

        var result = await Service(ctx).ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "jan.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        var batch = await ctx.Db.ImportBatches.AsNoTracking().SingleAsync();
        batch.ImportBatchId.ShouldBe(result.ImportBatchId!.Value);
        batch.FileName.ShouldBe("jan.qfx");
        batch.ParserSourceSystem.ShouldBe("QFX");
        batch.FileSha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(qfx))));
        batch.AccountId.ShouldBeNull();
        Gunzip(batch.Content!).ShouldBe(qfx);
        batch.SummaryJson.ShouldNotBeNull();

        (await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync()).ImportBatchId.ShouldBe(batch.ImportBatchId);
        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().SingleAsync()).ImportBatchId.ShouldBe(batch.ImportBatchId);
        (await ctx.Db.AccountHoldings.AsNoTracking().SingleAsync()).CreatedByImportBatchId.ShouldBe(batch.ImportBatchId);
        (await ctx.Db.Accounts.AsNoTracking().SingleAsync()).CreatedByImportBatchId.ShouldBe(batch.ImportBatchId);
        result.Accounts[0].SnapshotsInserted.ShouldBe(1);
    }

    [Fact]
    public async Task Parser_warnings_are_returned_stored_and_shown_again_when_the_file_is_re_uploaded()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var qfx = Qfx(Statement("ACCT-1", Buy("B-1", "20260105", "XYZ", 2, -200m)
            + "<FUTUREAGGREGATE><INVTRAN><FITID>F-1</FITID><DTTRADE>20260101</DTTRADE></INVTRAN></FUTUREAGGREGATE>"));
        var service = Service(ctx);

        var first = await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "a.qfx", CancellationToken.None);
        var again = await service.ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "a.qfx", CancellationToken.None);

        first.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.UnknownAggregate);
        again.Status.ShouldBe(PortfolioImportStatus.AlreadyImported);
        again.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.UnknownAggregate);
        again.Accounts.ShouldHaveSingleItem().Inserted.ShouldBe(1);
    }

    [Fact]
    public async Task A_row_the_parser_now_maps_differently_is_updated_and_the_change_recorded_on_the_batch()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        // A provider id (e.g. a FITID) identifies the row even when its amount changes.
        var before = Labelled(TransactionType.Other, amount: -25m) with { ExternalId = "R-1" };
        var after = Labelled(TransactionType.Reinvest, amount: -25.5m) with { ExternalId = "R-1" };

        await StubService(ctx, [before]).ImportToAccountAsync(account.AccountId, Stream("v1"), "old.xlsx", CancellationToken.None, "VANGUARD");
        var second = await StubService(ctx, [after]).ImportToAccountAsync(account.AccountId, Stream("v2"), "new.xlsx", CancellationToken.None, "VANGUARD");

        second.Accounts[0].Updated.ShouldBe(1);
        var stored = await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync();
        stored.Type.ShouldBe(TransactionType.Reinvest);
        stored.Amount.ShouldBe(-25.5m);
        var update = await ctx.Db.ImportBatchRowUpdates.AsNoTracking().SingleAsync();
        update.ImportBatchId.ShouldBe(second.ImportBatchId!.Value);
        update.AccountTransactionId.ShouldBe(stored.AccountTransactionId);
        update.Previous.Type.ShouldBe(TransactionType.Other);
        update.Previous.Amount.ShouldBe(-25m);
        update.Next.Type.ShouldBe(TransactionType.Reinvest);
    }

    [Fact]
    public async Task A_later_export_with_one_more_identical_row_adds_only_that_row()
    {
        // Occurrence ids: the stored "-0" and "-1" match their own ids, which must also use up their fingerprint,
        // so the third identical row isn't swallowed by the cross-source fingerprint check.
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var row = Labelled(TransactionType.Reinvest, amount: -25m);

        await StubService(ctx, [row, row]).ImportToAccountAsync(account.AccountId, Stream("v1"), "a.xlsx", CancellationToken.None, "VANGUARD");
        var second = await StubService(ctx, [row, row, row]).ImportToAccountAsync(account.AccountId, Stream("v2"), "b.xlsx", CancellationToken.None, "VANGUARD");

        second.Accounts[0].Inserted.ShouldBe(1);
        second.Accounts[0].Skipped.ShouldBe(2);
        var ids = await ctx.Db.AccountTransactions.AsNoTracking().Select(t => t.ExternalId).ToListAsync();
        ids.Select(id => id[^2..]).Order().ShouldBe(["-0", "-1", "-2"]);
    }

    [Fact]
    public async Task The_stored_file_can_be_fetched_back_exactly_as_uploaded_but_only_within_its_portfolio()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var qfx = Qfx(Statement("ACCT-1", Buy("B-1", "20260105", "XYZ", 2, -200m)));
        var result = await Service(ctx).ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "jan.qfx", CancellationToken.None);
        var history = new ImportHistoryService(ctx.Db);

        var file = await history.GetFileAsync(portfolio.PortfolioId, result.ImportBatchId!.Value, CancellationToken.None);

        file.ShouldNotBeNull();
        file.FileName.ShouldBe("jan.qfx");
        file.ContentType.ShouldBe("application/x-ofx");
        Encoding.UTF8.GetString(file.Content).ShouldBe(qfx);
        (await history.GetFileAsync(Guid.NewGuid(), result.ImportBatchId!.Value, CancellationToken.None)).ShouldBeNull();
        (await history.GetFileAsync(portfolio.PortfolioId, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    // ---------------- reprocess ----------------

    [Fact]
    public async Task Reprocessing_adds_rows_the_parser_used_to_drop_tagged_with_their_original_import()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var kept = Labelled(TransactionType.Reinvest, amount: -25m);
        var dropped = kept with { TradeDate = new DateOnly(2024, 12, 27), Amount = -30m };

        var first = await StubService(ctx, [kept]).ImportToAccountAsync(account.AccountId, Stream("v1"), "a.xlsx", CancellationToken.None, "VANGUARD");
        var result = await StubService(ctx, [kept, dropped]).ReprocessAsync(null, null, CancellationToken.None);

        result.Inserted.ShouldBe(1);
        result.PerBatch.ShouldHaveSingleItem().ImportBatchId.ShouldBe(first.ImportBatchId!.Value);
        var rows = await ctx.Db.AccountTransactions.AsNoTracking().ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.ImportBatchId == first.ImportBatchId);

        var batch = await ctx.Db.ImportBatches.AsNoTracking().SingleAsync();
        batch.ReprocessedAt.ShouldNotBeNull();
        var history = await new ImportHistoryService(ctx.Db).ListAsync(account.PortfolioId, CancellationToken.None);
        history!.Imports.Single().Accounts.Single().Inserted.ShouldBe(2);
    }

    [Fact]
    public async Task Reprocessing_skips_a_file_whose_parser_is_no_longer_registered()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        await StubService(ctx, [Labelled(TransactionType.Reinvest, -25m)])
            .ImportToAccountAsync(account.AccountId, Stream("v1"), "a.xlsx", CancellationToken.None, "VANGUARD");

        var result = await Service(ctx).ReprocessAsync(null, null, CancellationToken.None);

        result.Batches.ShouldBe(0);
        result.PerBatch.ShouldHaveSingleItem().Skipped.ShouldNotBeNull();
    }

    // ---------------- single-account QFX & routing (2.5) ----------------

    [Fact]
    public async Task A_QFX_on_an_account_imports_its_own_statement_and_skips_the_others_with_a_warning()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx, accountNumber: "aaa 111");
        var qfx = Qfx(
            Statement("AAA-111", Buy("A-1", "20260105", "XYZ", 1, -100m)),
            Statement("BBB-222", Buy("B-1", "20260105", "XYZ", 1, -100m)));

        var result = await Service(ctx).ImportToAccountAsync(account.AccountId, Stream(qfx), "both.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        (await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync()).ExternalId.ShouldBe("A-1");
        var warning = result.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(ImportWarningCodes.OtherAccountSkipped);
        warning.Samples.ShouldBe(["…B222"]);
        (await ctx.Db.ImportBatches.AsNoTracking().SingleAsync()).AccountId.ShouldBe(account.AccountId);
    }

    [Fact]
    public async Task A_portfolio_import_matches_a_hand_created_account_whatever_its_separators_and_case()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var manual = new Account(portfolio.PortfolioId, "Brokerage", "example.com", "acct 1");
        ctx.Db.Accounts.Add(manual);
        await ctx.Db.SaveChangesAsync();

        var result = await Service(ctx).ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Qfx(Statement("ACCT-1", Buy("B-1", "20260105", "XYZ", 1, -100m)))), "a.qfx", CancellationToken.None);

        result.Accounts.ShouldHaveSingleItem().AccountId.ShouldBe(manual.AccountId);
        result.Accounts[0].Created.ShouldBeFalse();
        (await ctx.Db.Accounts.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Two_statements_for_one_account_in_a_file_share_its_holdings_and_snapshots()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var qfx = Qfx(
            Statement("ACCT-1", Buy("B-1", "20260105", "NEWT", 1, -100m), positions: Position("NEWT", 1, 100m)),
            Statement("ACCT-1", Buy("B-2", "20260106", "NEWT", 1, -100m), positions: Position("NEWT", 2, 200m)));

        var result = await Service(ctx).ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "dup.qfx", CancellationToken.None);

        result.Accounts.ShouldHaveSingleItem().Inserted.ShouldBe(2);
        (await ctx.Db.AccountHoldings.AsNoTracking().CountAsync()).ShouldBe(1);
        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    // ---------------- statement cash ----------------

    [Fact]
    public async Task Available_cash_that_isnt_a_position_is_recorded_on_the_accounts_cash_holding()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var qfx = Qfx(Statement("ACCT-1", Buy("B-1", "20260105", "XYZ", 1, -100m),
            positions: Position("XYZ", 1, 110m), availableCash: 42.50m));

        var result = await Service(ctx).ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "a.qfx", CancellationToken.None);

        var cash = await ctx.Db.AccountHoldings.AsNoTracking().SingleAsync(h => h.Kind == AccountHoldingKind.Cash);
        cash.Symbol.ShouldBe(CashHoldings.Symbol);
        var snapshot = await ctx.Db.AccountHoldingSnapshots.AsNoTracking().SingleAsync(s => s.AccountHoldingId == cash.AccountHoldingId);
        snapshot.AsOf.ShouldBe(new DateOnly(2026, 6, 1));
        snapshot.MarketValue.ShouldBe(42.50m);
        snapshot.Source.ShouldBe(AccountHoldingSnapshotSource.BrokerPosition);
        result.Accounts[0].SnapshotsInserted.ShouldBe(2);
    }

    [Fact]
    public async Task At_Vanguard_available_cash_is_the_settlement_fund_so_it_isnt_recorded_twice()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var qfx = Qfx(Statement("ACCT-1", Buy("B-1", "20260105", "SETTL", 75, -75m, memo: "MONEY FUND PURCHASE"),
            positions: Position("SETTL", 75, 75m), availableCash: 75m, brokerId: "vanguard.com"));

        await Service(ctx).ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "v.qfx", CancellationToken.None);

        (await ctx.Db.AccountHoldings.AsNoTracking().AnyAsync(h => h.Kind == AccountHoldingKind.Cash)).ShouldBeFalse();
        (await ctx.Db.AccountHoldings.AsNoTracking().SingleAsync()).IsSettlementFund.ShouldBeTrue();
        (await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync()).IsSettlementFund.ShouldBeTrue();
    }

    // ---------------- stable ids (2.1) ----------------

    [Fact]
    public async Task Stored_synthetic_ids_are_rewritten_to_stable_occurrence_ids_once()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var date = new DateOnly(2024, 3, 1);
        var fingerprint = TransactionFingerprint.Compute(account.AccountId, date, "XYZ", 1m, -10m);
        // Two identical rows stored under the old row-number scheme (rows 7 and 3 of their files), and a FITID row.
        var first = Stored(account.AccountId, $"{fingerprint}-7", date);
        await Task.Delay(5);
        var second = Stored(account.AccountId, $"{fingerprint}-3", date);
        var provider = Stored(account.AccountId, "FITID-123", date, source: "QFX");
        ctx.Db.AccountTransactions.AddRange(first, second, provider);
        await ctx.Db.SaveChangesAsync();

        var migration = new StableExternalIdMigration(ctx.Db, NullLogger<StableExternalIdMigration>.Instance);
        (await migration.RunAsync(CancellationToken.None)).ShouldBe(2);
        (await migration.RunAsync(CancellationToken.None)).ShouldBe(0);

        var ids = await ctx.Db.AccountTransactions.AsNoTracking().ToDictionaryAsync(t => t.AccountTransactionId, t => t.ExternalId);
        ids[first.AccountTransactionId].ShouldBe($"{fingerprint}-0"); // imported first
        ids[second.AccountTransactionId].ShouldBe($"{fingerprint}-1");
        ids[provider.AccountTransactionId].ShouldBe("FITID-123");
    }

    // ---------------- helpers ----------------

    private static AccountTransaction Stored(Guid accountId, string externalId, DateOnly date, string source = "VANGUARD")
    {
        var row = new AccountTransaction(accountId, source, externalId, TransactionType.Buy, date, -10m);
        row.SetSecurityReference("XYZ", null);
        row.SetTradeDetails(1m, 10m, null, null);
        return row;
    }

    private static ParsedTransaction Labelled(TransactionType type, decimal amount) =>
        new(ExternalId: null, Type: type, TradeDate: new DateOnly(2024, 12, 20), SettlementDate: null,
            Ticker: "ZXBAX", Cusip: null, Quantity: 2m, Price: 12.5m, Amount: amount,
            Fees: null, CurrencyCode: null, Memo: null, SourceType: "Reinvestment (LT gain)");

    private static PortfolioImportService Service(TestDbContext ctx) =>
        new(ctx.Db, [new QfxFileParser()], NoImpliedContributions.Instance, NullLogger<PortfolioImportService>.Instance);

    private static PortfolioImportService StubService(TestDbContext ctx, IReadOnlyList<ParsedTransaction> rows) =>
        new(ctx.Db, [new LedgerStub("VANGUARD", rows)], NoImpliedContributions.Instance, NullLogger<PortfolioImportService>.Instance);


    /// <summary>A metadata-less parser emitting fixed rows, selected only by an explicit source system.</summary>
    private sealed class LedgerStub(string sourceSystem, IReadOnlyList<ParsedTransaction> rows) : IPortfolioFileParser
    {
        public string SourceSystem { get; } = sourceSystem;
        public string DisplayName => SourceSystem;
        public int Priority => 0;
        public IReadOnlyCollection<string> FileExtensions { get; } = [".dat"];

        public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new ParsedPortfolioFile(SourceSystem, [new ParsedAccountStatement(null, null, rows, [], null)]));
    }

    private static async Task<Portfolio> SeedPortfolioAsync(TestDbContext ctx)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio;
    }

    private static async Task<Account> SeedAccountAsync(TestDbContext ctx, string accountNumber = "1234")
    {
        var portfolio = await SeedPortfolioAsync(ctx);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "example.com", accountNumber);
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account;
    }

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));

    private static string Gunzip(byte[] content)
    {
        using var gzip = new GZipStream(new MemoryStream(content), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static string Buy(string fitId, string date, string ticker, decimal units, decimal total, string memo = "BUY") => $"""
        <BUYMF><INVBUY>
          <INVTRAN><FITID>{fitId}</FITID><DTTRADE>{date}</DTTRADE><MEMO>{memo}</MEMO></INVTRAN>
          <SECID><UNIQUEID>{ticker}</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
          <UNITS>{units}</UNITS><UNITPRICE>{Math.Abs(total) / units}</UNITPRICE><TOTAL>{total}</TOTAL>
        </INVBUY><BUYTYPE>BUY</BUYTYPE></BUYMF>
        """;

    internal static string Position(string ticker, decimal units, decimal marketValue) => $"""
        <POSMF><INVPOS>
          <SECID><UNIQUEID>{ticker}</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
          <HELDINACCT>CASH</HELDINACCT><POSTYPE>LONG</POSTYPE><UNITS>{units}</UNITS><UNITPRICE>{marketValue / units}</UNITPRICE><MKTVAL>{marketValue}</MKTVAL>
        </INVPOS></POSMF>
        """;

    internal static string Statement(
        string accountNumber, string transactions, string positions = "", decimal? availableCash = null, string brokerId = "example.com") => $"""
        <INVSTMTTRNRS><TRNUID>1</TRNUID><INVSTMTRS>
          <DTASOF>20260601120000</DTASOF><CURDEF>USD</CURDEF>
          <INVACCTFROM><BROKERID>{brokerId}</BROKERID><ACCTID>{accountNumber}</ACCTID></INVACCTFROM>
          <INVTRANLIST><DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>{transactions}</INVTRANLIST>
          {(positions.Length > 0 ? $"<INVPOSLIST>{positions}</INVPOSLIST>" : "")}
          {(availableCash is { } c ? $"<INVBAL><AVAILCASH>{c}</AVAILCASH><MARGINBALANCE>0</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>" : "")}
        </INVSTMTRS></INVSTMTTRNRS>
        """;

    internal static string Qfx(params string[] statements) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
        <OFX><INVSTMTMSGSRSV1>{string.Concat(statements)}</INVSTMTMSGSRSV1></OFX>
        """;
}
