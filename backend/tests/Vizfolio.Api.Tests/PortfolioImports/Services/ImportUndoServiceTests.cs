using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Domain.Portfolios;
using static Vizfolio.Api.Tests.PortfolioImports.Services.ImportBatchTests;

namespace Vizfolio.Api.Tests.PortfolioImports.Services;

/// <summary>
/// Undoing an import (roadmap Phase 2.7, Appendix C.6): it removes exactly what the file added, restores what it
/// changed, cleans up what it created, brings back what later files skipped because of it, and is recorded.
/// </summary>
public sealed class ImportUndoServiceTests
{
    private static readonly DateOnly Day1 = new(2025, 1, 6);
    private static readonly DateOnly Day2 = new(2025, 2, 3);

    [Fact]
    public async Task Undo_removes_only_the_files_rows_and_keeps_another_sources_overlapping_rows()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var earlier = await ImportAsync(ctx, account, "SRCB", [Deposit(Day1, 500m)]);
        var file = await ImportAsync(ctx, account, "SRCA", [Deposit(Day1, 500m), Deposit(Day2, 200m)]);

        var outcome = await Undo(ctx).UndoAsync(account.PortfolioId, file.ImportBatchId!.Value, CancellationToken.None);

        outcome.Status.ShouldBe(ImportUndoStatus.Ok);
        outcome.Summary!.Transactions.ShouldBe(1);
        var rows = await ctx.Db.AccountTransactions.AsNoTracking().ToListAsync();
        rows.ShouldHaveSingleItem().ImportBatchId.ShouldBe(earlier.ImportBatchId);
    }

    [Fact]
    public async Task Undoing_an_earlier_file_brings_back_rows_a_later_file_skipped_as_its_duplicates()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var a = await ImportAsync(ctx, account, "SRCA", [Deposit(Day1, 500m)]);
        var b = await ImportAsync(ctx, account, "SRCB", [Deposit(Day1, 500m), Deposit(Day2, 200m)]);
        b.Accounts[0].Skipped.ShouldBe(1);

        var preview = await Undo(ctx).PreviewAsync(account.PortfolioId, a.ImportBatchId!.Value, CancellationToken.None);
        preview.Summary!.LaterImportsReplayed.ShouldBe(["SRCB.dat"]);

        await Undo(ctx).UndoAsync(account.PortfolioId, a.ImportBatchId!.Value, CancellationToken.None);

        // The same ledger as importing B alone.
        var rows = await ctx.Db.AccountTransactions.AsNoTracking().OrderBy(t => t.TradeDate).ToListAsync();
        rows.Select(r => (r.SourceSystem, r.TradeDate, r.Amount)).ShouldBe([("SRCB", Day1, 500m), ("SRCB", Day2, 200m)]);
        rows.ShouldAllBe(r => r.ImportBatchId == b.ImportBatchId);
    }

    [Fact]
    public async Task Undo_restores_rows_the_file_updated_unless_a_later_file_changed_them_again()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        await ImportAsync(ctx, account, "VANGUARD", [Labelled("R-1", TransactionType.Other), Labelled("R-2", TransactionType.Other)], "v1");
        var fix = await ImportAsync(ctx, account, "VANGUARD", [Labelled("R-1", TransactionType.Reinvest), Labelled("R-2", TransactionType.Reinvest)], "v2");
        await ImportAsync(ctx, account, "VANGUARD", [Labelled("R-2", TransactionType.CapitalGain)], "v3");

        var preview = await Undo(ctx).PreviewAsync(account.PortfolioId, fix.ImportBatchId!.Value, CancellationToken.None);
        preview.Summary!.UpdatesReverted.ShouldBe(1);

        await Undo(ctx, replayWith: []).UndoAsync(account.PortfolioId, fix.ImportBatchId!.Value, CancellationToken.None);

        var types = await ctx.Db.AccountTransactions.AsNoTracking().ToDictionaryAsync(t => t.ExternalId, t => t.Type);
        types["R-1"].ShouldBe(TransactionType.Other);
        types["R-2"].ShouldBe(TransactionType.CapitalGain);
    }

    [Fact]
    public async Task Undo_removes_an_account_the_file_created_but_keeps_an_existing_account_it_imported_into()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var existing = new Account(portfolio.PortfolioId, "Mine", "example.com", "OLD-1");
        ctx.Db.Accounts.Add(existing);
        await ctx.Db.SaveChangesAsync();
        var qfx = Qfx(
            Statement("OLD-1", Buy("O-1", "20260105", "XYZ", 1, -100m)),
            Statement("NEW-2", Buy("N-1", "20260105", "XYZ", 1, -100m), positions: Position("XYZ", 1, 110m)));
        var result = await Importer(ctx).ImportToPortfolioAsync(portfolio.PortfolioId, Stream(qfx), "both.qfx", CancellationToken.None);

        var preview = await Undo(ctx).PreviewAsync(portfolio.PortfolioId, result.ImportBatchId!.Value, CancellationToken.None);
        var outcome = await Undo(ctx).UndoAsync(portfolio.PortfolioId, result.ImportBatchId!.Value, CancellationToken.None);

        preview.Summary!.AccountsRemoved.ShouldBe(1);
        preview.Summary.HoldingsRemoved.ShouldBe(2);
        preview.Summary.Snapshots.ShouldBe(1);
        outcome.Summary!.AccountsRemoved.ShouldBe(1);
        outcome.Summary.HoldingsRemoved.ShouldBe(2);
        (await ctx.Db.Accounts.AsNoTracking().SingleAsync()).AccountId.ShouldBe(existing.AccountId);
        (await ctx.Db.AccountHoldings.AsNoTracking().CountAsync()).ShouldBe(0);
        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Undo_keeps_a_holding_the_file_created_when_an_opening_balance_now_uses_it()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var file = await ImportAsync(ctx, account, "SRCA", [BuyRow(Day2, "XYZ", 1m, -100m)]);
        var holding = await ctx.Db.AccountHoldings.SingleAsync();
        var opening = new AccountHoldingSnapshot(holding.AccountHoldingId, Day1, 3m, AccountHoldingSnapshotSource.OpeningBalance);
        ctx.Db.AccountHoldingSnapshots.Add(opening);
        await ctx.Db.SaveChangesAsync();

        var outcome = await Undo(ctx).UndoAsync(account.PortfolioId, file.ImportBatchId!.Value, CancellationToken.None);

        outcome.Summary!.HoldingsRemoved.ShouldBe(0);
        (await ctx.Db.AccountHoldings.AsNoTracking().SingleAsync()).AccountHoldingId.ShouldBe(holding.AccountHoldingId);
        (await ctx.Db.AccountHoldingSnapshots.AsNoTracking().SingleAsync()).Source.ShouldBe(AccountHoldingSnapshotSource.OpeningBalance);
    }

    [Fact]
    public async Task Undo_re_derives_implied_contributions_for_the_account()
    {
        // The undone file held the deposit that funded a later purchase: without it the purchase is implied
        // to be funded from outside again.
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var deposit = await ImportAsync(ctx, account, "SRCA", [Deposit(Day1, 1000m)], implied: true);
        await ImportAsync(ctx, account, "SRCB", [BuyRow(Day2, "XYZ", 10m, -1000m)], implied: true);
        (await ImpliedAsync(ctx)).ShouldBeEmpty();

        await Undo(ctx, implied: true).UndoAsync(account.PortfolioId, deposit.ImportBatchId!.Value, CancellationToken.None);

        (await ImpliedAsync(ctx)).ShouldHaveSingleItem().Amount.ShouldBe(1000m);
    }

    [Fact]
    public async Task An_undone_import_cant_be_undone_twice_and_its_file_can_be_imported_again()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var file = await ImportAsync(ctx, account, "SRCA", [Deposit(Day1, 500m)]);

        (await Undo(ctx).UndoAsync(account.PortfolioId, file.ImportBatchId!.Value, CancellationToken.None)).Status.ShouldBe(ImportUndoStatus.Ok);
        (await Undo(ctx).UndoAsync(account.PortfolioId, file.ImportBatchId!.Value, CancellationToken.None)).Status.ShouldBe(ImportUndoStatus.AlreadyUndone);
        (await Undo(ctx).PreviewAsync(account.PortfolioId, file.ImportBatchId!.Value, CancellationToken.None)).Status.ShouldBe(ImportUndoStatus.AlreadyUndone);

        var batch = await ctx.Db.ImportBatches.AsNoTracking().SingleAsync();
        batch.Status.ShouldBe(ImportBatchStatus.Undone);
        batch.UndoneAt.ShouldNotBeNull();

        var again = await ImportAsync(ctx, account, "SRCA", [Deposit(Day1, 500m)]);
        again.Status.ShouldBe(PortfolioImportStatus.Success);
        again.Accounts[0].Inserted.ShouldBe(1);
    }

    [Fact]
    public async Task Undo_never_touches_rows_imported_before_imports_were_recorded()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var legacy = new AccountTransaction(account.AccountId, "SRCA", "legacy-1", TransactionType.Deposit, Day1, 500m);
        ctx.Db.AccountTransactions.Add(legacy);
        await ctx.Db.SaveChangesAsync();
        var file = await ImportAsync(ctx, account, "SRCB", [Deposit(Day1, 500m), Deposit(Day2, 50m)]);

        await Undo(ctx).UndoAsync(account.PortfolioId, file.ImportBatchId!.Value, CancellationToken.None);

        (await ctx.Db.AccountTransactions.AsNoTracking().SingleAsync()).AccountTransactionId.ShouldBe(legacy.AccountTransactionId);
        var history = await new ImportHistoryService(ctx.Db).ListAsync(account.PortfolioId, CancellationToken.None);
        history!.TransactionsImportedBeforeHistory.ShouldBe(1);
        history.Imports.ShouldHaveSingleItem().Status.ShouldBe("Undone");
    }

    [Fact]
    public async Task An_import_from_another_portfolio_is_not_found()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var account = await SeedAccountAsync(ctx);
        var file = await ImportAsync(ctx, account, "SRCA", [Deposit(Day1, 500m)]);

        (await Undo(ctx).PreviewAsync(Guid.NewGuid(), file.ImportBatchId!.Value, CancellationToken.None)).Status.ShouldBe(ImportUndoStatus.NotFound);
        (await Undo(ctx).UndoAsync(account.PortfolioId, Guid.NewGuid(), CancellationToken.None)).Status.ShouldBe(ImportUndoStatus.NotFound);
    }

    // ---------------- helpers ----------------

    /// <summary>Stub parsers by source; a test's later imports re-register the same sources for replay.</summary>
    private readonly Dictionary<string, IReadOnlyList<ParsedTransaction>> _rowsBySource = new();

    private async Task<PortfolioImportResult> ImportAsync(
        TestDbContext ctx, Account account, string source, IReadOnlyList<ParsedTransaction> rows, string? content = null, bool implied = false)
    {
        _rowsBySource[source] = rows;
        var service = new PortfolioImportService(
            ctx.Db, [new StubParser(source, rows)], Implied(ctx, implied), NullLogger<PortfolioImportService>.Instance,
            new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance));
        return await service.ImportToAccountAsync(account.AccountId, Stream(content ?? source), $"{source}.dat", CancellationToken.None, source);
    }

    private ImportUndoService Undo(TestDbContext ctx, IReadOnlyList<string>? replayWith = null, bool implied = false)
    {
        // Replays use each source's latest rows (what its file contains now), unless a test pins none.
        var parsers = (replayWith is null ? _rowsBySource : new Dictionary<string, IReadOnlyList<ParsedTransaction>>())
            .Select(kv => (IPortfolioFileParser)new StubParser(kv.Key, kv.Value))
            .Append(new QfxFileParser())
            .ToList();
        var relinker = new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance);
        var imports = new PortfolioImportService(ctx.Db, parsers, Implied(ctx, implied), NullLogger<PortfolioImportService>.Instance, relinker);
        return new ImportUndoService(ctx.Db, imports, Implied(ctx, implied), relinker);
    }

    private static PortfolioImportService Importer(TestDbContext ctx) =>
        new(ctx.Db, [new QfxFileParser()], Implied(ctx, false), NullLogger<PortfolioImportService>.Instance,
            new LedgerRelinker(ctx.Db, NullLogger<LedgerRelinker>.Instance));

    private static IImpliedContributionService Implied(TestDbContext ctx, bool real) => real
        ? new ImpliedContributionService(ctx.Db, new AccountValuationLoader(ctx.Db, new ValuationOptions()))
        : new NoImpliedContributions();

    private static Task<List<AccountTransaction>> ImpliedAsync(TestDbContext ctx)
        => ctx.Db.AccountTransactions.AsNoTracking()
            .Where(t => t.SourceSystem == ImpliedContributionService.SourceSystem)
            .ToListAsync();

    private sealed class NoImpliedContributions : IImpliedContributionService
    {
        public Task<ImpliedContributionPreview?> PreviewForAccountAsync(Guid portfolioId, Guid accountId, CancellationToken cancellationToken)
            => Task.FromResult<ImpliedContributionPreview?>(null);

        public Task<ImpliedContributionSyncResult> SyncForAccountAsync(Guid accountId, CancellationToken cancellationToken)
            => Task.FromResult(new ImpliedContributionSyncResult(0, 0m));
    }

    private sealed class StubParser(string sourceSystem, IReadOnlyList<ParsedTransaction> rows) : IPortfolioFileParser
    {
        public string SourceSystem { get; } = sourceSystem;
        public string DisplayName => SourceSystem;
        public int Priority => 0;
        public IReadOnlyCollection<string> FileExtensions { get; } = [".dat"];

        public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new ParsedPortfolioFile(SourceSystem, [new ParsedAccountStatement(null, null, rows, [], null)]));
    }

    private static ParsedTransaction Deposit(DateOnly date, decimal amount) =>
        new(null, TransactionType.Deposit, date, null, null, null, null, null, amount, null, null, null);

    private static ParsedTransaction BuyRow(DateOnly date, string ticker, decimal quantity, decimal amount) =>
        new(null, TransactionType.Buy, date, null, ticker, null, quantity, null, amount, null, null, null);

    private static ParsedTransaction Labelled(string id, TransactionType type) =>
        new(id, type, new DateOnly(2024, 12, 20), null, "ZXBAX", null, 2m, 12.5m, -25m, null, null, null, "Reinvestment (LT gain)");

    private static async Task<Portfolio> SeedPortfolioAsync(TestDbContext ctx)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio;
    }

    private static async Task<Account> SeedAccountAsync(TestDbContext ctx)
    {
        var portfolio = await SeedPortfolioAsync(ctx);
        var account = new Account(portfolio.PortfolioId, "Brokerage", "example.com", "1234");
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account;
    }

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));
}
