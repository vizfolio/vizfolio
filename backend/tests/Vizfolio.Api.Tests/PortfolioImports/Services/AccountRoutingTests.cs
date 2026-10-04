using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Api.Tests.PortfolioImports.Fakes;
using Vizfolio.Application.PortfolioImports;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Services;

/// <summary>
/// Routing an upload to its account (roadmap 2.6): a file without account details goes to the account that already
/// holds its transactions; when the evidence isn't clear the import asks; an upload to the wrong account is caught; and
/// a QFX whose number doesn't match any account but whose history does asks before creating a duplicate account.
/// </summary>
public sealed class AccountRoutingTests
{
    // ---------------- the decision rule ----------------

    private static readonly ImportRoutingOptions Defaults = new();

    private static RoutingAnalysis Analysis(params (int Matching, int InRange)[] candidates) => new(
        100, null, null,
        candidates.Select((c, i) => new RoutingCandidate(Guid.NewGuid(), $"Account {i}", "vanguard.com", "…0000", c.Matching, c.InRange, 0))
            .ToList());

    [Theory]
    [InlineData(5, 5, 0, true)]     // enough rows, no competition, all of its in-range rows match
    [InlineData(4, 4, 0, false)]    // below MinMatchingRows
    [InlineData(10, 10, 5, true)]   // exactly twice the runner-up
    [InlineData(10, 10, 6, false)]  // less than twice the runner-up
    [InlineData(10, 20, 0, true)]   // half of the in-range rows match (a QFX-overlap with unmatched sweep twins)
    [InlineData(10, 21, 0, false)]  // under half: a few coincidental rows inside a busy history
    public void StrongMatch_needs_enough_rows_a_clear_lead_and_a_consistent_overlap(
        int matching, int inRange, int runnerUp, bool strong)
    {
        var analysis = Analysis((matching, inRange), (runnerUp, 50));

        var result = AccountRoutingAnalyzer.StrongMatch(analysis, Defaults);

        (result == analysis.Candidates[0].AccountId).ShouldBe(strong);
    }

    [Fact]
    public void StrongMatch_is_null_on_a_tie_and_when_there_are_no_accounts()
    {
        AccountRoutingAnalyzer.StrongMatch(Analysis((12, 12), (12, 12)), Defaults).ShouldBeNull();
        AccountRoutingAnalyzer.StrongMatch(Analysis(), Defaults).ShouldBeNull();
    }

    [Fact]
    public void StrongMatch_follows_configured_thresholds()
    {
        var strict = new ImportRoutingOptions { MinMatchingRows = 20, MinLeadRatio = 5m, MinOverlapShare = 0.9m };

        AccountRoutingAnalyzer.StrongMatch(Analysis((19, 19), (0, 0)), strict).ShouldBeNull();
        AccountRoutingAnalyzer.StrongMatch(Analysis((25, 25), (6, 6)), strict).ShouldBeNull();
        AccountRoutingAnalyzer.StrongMatch(Analysis((25, 30), (5, 5)), strict).ShouldBeNull();
        AccountRoutingAnalyzer.StrongMatch(Analysis((27, 30), (5, 5)), strict).ShouldNotBeNull();
    }

    [Fact]
    public void Options_bind_from_the_Imports_Routing_section()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Imports:Routing:Mode"] = "Confirm",
            ["Imports:Routing:MinMatchingRows"] = "8",
            ["Imports:Routing:MinLeadRatio"] = "3",
            ["Imports:Routing:MinOverlapShare"] = "0.75",
            ["Imports:Routing:GuardAccountImports"] = "false",
        }).Build();

        var options = configuration.GetSection(ImportRoutingOptions.SectionName).Get<ImportRoutingOptions>()!;

        options.Mode.ShouldBe(ImportRoutingMode.Confirm);
        options.MinMatchingRows.ShouldBe(8);
        options.MinLeadRatio.ShouldBe(3m);
        options.MinOverlapShare.ShouldBe(0.75m);
        options.GuardAccountImports.ShouldBeFalse();
    }

    // ---------------- portfolio uploads without account details ----------------

    [Fact]
    public async Task A_re_export_without_account_details_goes_to_the_account_that_holds_its_transactions()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolio, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);
        await SeedHistoryAsync(service, ira, IraRows);
        await SeedHistoryAsync(service, taxable, TaxableRows);

        // A later export: the same history plus two new months.
        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv([.. IraRows, .. NewIraRows])), "ira-later.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        var account = result.Accounts.ShouldHaveSingleItem();
        account.AccountId.ShouldBe(ira.AccountId);
        account.Inserted.ShouldBe(NewIraRows.Length);
        account.Skipped.ShouldBe(IraRows.Length);
        account.Routing.ShouldBe(new AccountRouting(RoutingMethods.Fingerprint, IraRows.Length));
        account.FirstDate.ShouldBe(IraRows[0].Date);
        account.LastDate.ShouldBe(NewIraRows[^1].Date);
        result.ParserDisplayName.ShouldBe("Generic CSV ledger (test)");

        // Recorded against the account, so a reprocess returns there and a re-upload is recognised.
        var batch = await ctx.Db.ImportBatches.AsNoTracking().SingleAsync(b => b.ImportBatchId == result.ImportBatchId);
        batch.AccountId.ShouldBe(ira.AccountId);
        var again = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv([.. IraRows, .. NewIraRows])), "ira-later.csv", CancellationToken.None);
        again.Status.ShouldBe(PortfolioImportStatus.AlreadyImported);
        var reprocess = await service.ReprocessAsync(portfolio.PortfolioId, [batch.ImportBatchId], CancellationToken.None);
        reprocess.PerBatch.ShouldHaveSingleItem().Skipped.ShouldBeNull();
        (await ctx.Db.AccountTransactions.CountAsync(t => t.AccountId == taxable.AccountId)).ShouldBe(TaxableRows.Length);
    }

    [Fact]
    public async Task Confirm_mode_asks_even_for_a_clear_match_and_pre_selects_it()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolio, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx, new ImportRoutingOptions { Mode = ImportRoutingMode.Confirm });
        await SeedHistoryAsync(service, ira, IraRows);
        await SeedHistoryAsync(service, taxable, TaxableRows);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv([.. IraRows, .. NewIraRows])), "ira.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.NeedsAccountSelection);
        var selection = result.Selections.ShouldHaveSingleItem();
        selection.SuggestedAccountId.ShouldBe(ira.AccountId);
        selection.Candidates[0].AccountId.ShouldBe(ira.AccountId);
        selection.Candidates[0].MatchingRows.ShouldBe(IraRows.Length);
        selection.Candidates[1].MatchingRows.ShouldBe(0);
        selection.Rows.ShouldBe(IraRows.Length + NewIraRows.Length);
        selection.FirstDate.ShouldBe(IraRows[0].Date);
    }

    [Fact]
    public async Task A_new_accounts_file_with_a_few_coincidental_matches_asks_instead_of_routing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolio, ira, _) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);
        await SeedHistoryAsync(service, ira, IraRows);

        // Another account's history over the same months that happens to repeat two of the IRA's rows exactly.
        Row[] newAccountRows = [.. TaxableRows, IraRows[2], IraRows[7]];
        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv(newAccountRows)), "new.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.NeedsAccountSelection);
        var selection = result.Selections.ShouldHaveSingleItem();
        selection.SuggestedAccountId.ShouldBeNull();
        selection.Candidates[0].MatchingRows.ShouldBe(2);
        (await ctx.Db.AccountTransactions.CountAsync()).ShouldBe(IraRows.Length);
    }

    [Fact]
    public async Task Identical_histories_in_two_accounts_are_a_tie_so_it_asks()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolio, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);
        await SeedHistoryAsync(service, ira, IraRows);
        await SeedHistoryAsync(service, taxable, IraRows);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv([.. IraRows, .. NewIraRows])), "which.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.NeedsAccountSelection);
        result.Selections.ShouldHaveSingleItem().SuggestedAccountId.ShouldBeNull();
    }

    [Fact]
    public async Task The_chosen_account_receives_the_file_when_it_is_sent_again()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolio, _, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv(TaxableRows)), "taxable.csv", CancellationToken.None,
            choices: Assign(new StatementAssignment("", taxable.AccountId, null)));

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        var account = result.Accounts.ShouldHaveSingleItem();
        account.AccountId.ShouldBe(taxable.AccountId);
        account.Inserted.ShouldBe(TaxableRows.Length);
        account.Routing!.Method.ShouldBe(RoutingMethods.UserSelected);
    }

    [Fact]
    public async Task A_new_account_for_a_file_without_account_details_needs_its_institution_and_number()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var service = NewService(ctx);

        var missing = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv(IraRows)), "ira.csv", CancellationToken.None,
            choices: Assign(new StatementAssignment("", null, new NewAccountDetails("IRA", "vanguard.com", " - "))));
        missing.Status.ShouldBe(PortfolioImportStatus.InvalidAccountSelection);
        missing.Error.ShouldNotBeNullOrWhiteSpace();

        var created = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv(IraRows)), "ira.csv", CancellationToken.None,
            choices: Assign(new StatementAssignment("", null, new NewAccountDetails("Rollover IRA", "Vanguard.com", "9876-5432"))));

        created.Status.ShouldBe(PortfolioImportStatus.Success);
        var result = created.Accounts.ShouldHaveSingleItem();
        result.Created.ShouldBeTrue();
        result.Routing!.Method.ShouldBe(RoutingMethods.Created);
        var account = await ctx.Db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == result.AccountId);
        account.Name.ShouldBe("Rollover IRA");
        account.InstitutionCode.ShouldBe("vanguard.com");
        account.AccountNumber.ShouldBe("9876-5432");
        account.CreatedByImportBatchId.ShouldBe(created.ImportBatchId);
    }

    [Fact]
    public async Task A_new_account_whose_number_already_exists_imports_into_that_account()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolio, ira, _) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv(IraRows)), "ira.csv", CancellationToken.None,
            choices: Assign(new StatementAssignment("", null, new NewAccountDetails("IRA again", "vanguard.com", "aaa 111"))));

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.Accounts.ShouldHaveSingleItem().AccountId.ShouldBe(ira.AccountId);
        (await ctx.Db.Accounts.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Choosing_an_account_outside_the_portfolio_is_rejected()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var service = NewService(ctx);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(Csv(IraRows)), "ira.csv", CancellationToken.None,
            choices: Assign(new StatementAssignment("", Guid.NewGuid(), null)));

        result.Status.ShouldBe(PortfolioImportStatus.InvalidAccountSelection);
    }

    // ---------------- the account-tab guard ----------------

    [Fact]
    public async Task Uploading_a_file_to_the_wrong_account_asks_before_importing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);
        await SeedHistoryAsync(service, ira, IraRows);

        var result = await service.ImportToAccountAsync(
            taxable.AccountId, Stream(Csv([.. IraRows, .. NewIraRows])), "ira.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.LikelyOtherAccount);
        var selection = result.Selections.ShouldHaveSingleItem();
        selection.Reason.ShouldBe(AccountSelectionReasons.LikelyOtherAccount);
        selection.SuggestedAccountId.ShouldBe(ira.AccountId);
        selection.Candidates.Select(c => c.AccountId).ShouldContain(taxable.AccountId);
        (await ctx.Db.AccountTransactions.CountAsync(t => t.AccountId == taxable.AccountId)).ShouldBe(0);
        (await ctx.Db.ImportBatches.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Import_here_anyway_skips_the_guard()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);
        await SeedHistoryAsync(service, ira, IraRows);

        var result = await service.ImportToAccountAsync(
            taxable.AccountId, Stream(Csv(IraRows)), "ira.csv", CancellationToken.None,
            choices: new ImportChoices { IgnoreRoutingCheck = true });

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.Accounts.ShouldHaveSingleItem().Routing!.Method.ShouldBe(RoutingMethods.Uploaded);
        (await ctx.Db.AccountTransactions.CountAsync(t => t.AccountId == taxable.AccountId)).ShouldBe(IraRows.Length);
    }

    [Fact]
    public async Task The_guard_can_be_turned_off_in_configuration()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx, new ImportRoutingOptions { GuardAccountImports = false });
        await SeedHistoryAsync(service, ira, IraRows);

        var result = await service.ImportToAccountAsync(taxable.AccountId, Stream(Csv(IraRows)), "ira.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
    }

    [Fact]
    public async Task Uploading_to_the_right_account_imports_without_asking()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (_, ira, taxable) = await SeedTwoAccountsAsync(ctx);
        var service = NewService(ctx);
        await SeedHistoryAsync(service, ira, IraRows);
        await SeedHistoryAsync(service, taxable, TaxableRows);

        var result = await service.ImportToAccountAsync(
            ira.AccountId, Stream(Csv([.. IraRows, .. NewIraRows])), "ira.csv", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.Accounts.ShouldHaveSingleItem().Inserted.ShouldBe(NewIraRows.Length);
    }

    // ---------------- QFX: a new number that looks like an existing account ----------------

    private static string QfxForAccount111 => """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1>
    <INVSTMTTRNRS><TRNUID>1</TRNUID>
      <INVSTMTRS>
        <DTASOF>20260601120000</DTASOF>
        <CURDEF>USD</CURDEF>
        <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>AAA-111</ACCTID></INVACCTFROM>
        <INVTRANLIST>
          <DTSTART>20250101</DTSTART><DTEND>20260601</DTEND>
""" + QfxBuys + """
        </INVTRANLIST>
      </INVSTMTRS>
    </INVSTMTTRNRS>
  </INVSTMTMSGSRSV1>
</OFX>
""";

    // The IRA's first six purchases, as the QFX reports them.
    private static string QfxBuys => string.Concat(IraRows.Take(6).Select((r, i) => $"""
          <BUYMF>
            <INVBUY>
              <INVTRAN><FITID>Q-{i}</FITID><DTTRADE>{r.Date:yyyyMMdd}</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>{r.Ticker}</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <UNITS>{r.Quantity.ToString(CultureInfo.InvariantCulture)}</UNITS><UNITPRICE>10</UNITPRICE><TOTAL>{r.Amount.ToString(CultureInfo.InvariantCulture)}</TOTAL>
            </INVBUY>
            <BUYTYPE>BUY</BUYTYPE>
          </BUYMF>
"""));

    [Fact]
    public async Task A_QFX_whose_number_matches_no_account_but_whose_history_does_asks_before_creating_one()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var typo = new Account(portfolio.PortfolioId, "IRA", "vanguard.com", "AAA-117");
        ctx.Db.Accounts.Add(typo);
        await ctx.Db.SaveChangesAsync();
        var service = NewService(ctx);
        await SeedHistoryAsync(service, typo, IraRows);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(QfxForAccount111), "ira.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.NeedsAccountSelection);
        var selection = result.Selections.ShouldHaveSingleItem();
        selection.Reason.ShouldBe(AccountSelectionReasons.UnknownAccountNumber);
        selection.FileAccountNumber.ShouldBe("AAA111");
        selection.SuggestedAccountId.ShouldBe(typo.AccountId);
        (await ctx.Db.Accounts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Choosing_the_lookalike_account_corrects_its_number_and_deduplicates_into_it()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var typo = new Account(portfolio.PortfolioId, "IRA", "vanguard.com", "AAA-117");
        ctx.Db.Accounts.Add(typo);
        await ctx.Db.SaveChangesAsync();
        var service = NewService(ctx);
        await SeedHistoryAsync(service, typo, IraRows);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(QfxForAccount111), "ira.qfx", CancellationToken.None,
            choices: Assign(new StatementAssignment("AAA-111", typo.AccountId, null)));

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        var account = result.Accounts.ShouldHaveSingleItem();
        account.AccountId.ShouldBe(typo.AccountId);
        account.Inserted.ShouldBe(0);
        account.Routing!.Method.ShouldBe(RoutingMethods.UserSelected);
        (await ctx.Db.Accounts.AsNoTracking().SingleAsync()).AccountNumber.ShouldBe("AAA-111");

        // From now on the account's QFX routes by number.
        var reprocess = await service.ReprocessAsync(portfolio.PortfolioId, [result.ImportBatchId!.Value], CancellationToken.None);
        reprocess.PerBatch.ShouldHaveSingleItem().Skipped.ShouldBeNull();
        (await ctx.Db.Accounts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Choosing_a_new_account_for_the_QFX_creates_it_with_the_files_number()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var typo = new Account(portfolio.PortfolioId, "IRA", "vanguard.com", "AAA-117");
        ctx.Db.Accounts.Add(typo);
        await ctx.Db.SaveChangesAsync();
        var service = NewService(ctx);
        await SeedHistoryAsync(service, typo, IraRows);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(QfxForAccount111), "ira.qfx", CancellationToken.None,
            choices: Assign(new StatementAssignment("AAA111", null, new NewAccountDetails("Spouse IRA", null, null))));

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        var created = result.Accounts.ShouldHaveSingleItem();
        created.Created.ShouldBeTrue();
        created.AccountNumber.ShouldBe("AAA-111");
        (await ctx.Db.Accounts.AsNoTracking().SingleAsync(a => a.AccountId == created.AccountId)).Name.ShouldBe("Spouse IRA");
    }

    [Fact]
    public async Task A_QFX_for_a_genuinely_new_account_is_created_without_asking()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolio = await SeedPortfolioAsync(ctx);
        var other = new Account(portfolio.PortfolioId, "Taxable", "vanguard.com", "BBB-222");
        ctx.Db.Accounts.Add(other);
        await ctx.Db.SaveChangesAsync();
        var service = NewService(ctx);
        await SeedHistoryAsync(service, other, TaxableRows);

        var result = await service.ImportToPortfolioAsync(
            portfolio.PortfolioId, Stream(QfxForAccount111), "ira.qfx", CancellationToken.None);

        result.Status.ShouldBe(PortfolioImportStatus.Success);
        result.Accounts.ShouldHaveSingleItem().Routing!.Method.ShouldBe(RoutingMethods.Created);
    }

    // ---------------- fixtures ----------------

    private sealed record Row(DateOnly Date, string Ticker, decimal Quantity, decimal Amount);

    private static Row[] MonthlyBuys(string ticker, int year, int months, decimal baseAmount, int startMonth = 1) =>
        Enumerable.Range(0, months)
            .Select(i => new Row(new DateOnly(year, startMonth + i, 15), ticker, 1.5m + i, -(baseAmount + 10 * i)))
            .ToArray();

    private static readonly Row[] IraRows = MonthlyBuys("ZXAAA", 2025, 12, 100m);
    private static readonly Row[] NewIraRows = MonthlyBuys("ZXAAA", 2026, 2, 300m);
    private static readonly Row[] TaxableRows = MonthlyBuys("ZXBBB", 2025, 12, 205m);

    private static string Csv(IEnumerable<Row> rows)
    {
        var csv = new StringBuilder("Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo\n");
        foreach (var r in rows)
            csv.Append(CultureInfo.InvariantCulture,
                $"{r.Date:yyyy-MM-dd},Buy,{r.Ticker},{r.Quantity},10,{r.Amount},0,USD,Buy\n");
        return csv.ToString();
    }

    private static ImportChoices Assign(params StatementAssignment[] assignments) => new() { Assignments = assignments };

    private static async Task SeedHistoryAsync(PortfolioImportService service, Account account, Row[] rows)
    {
        var result = await service.ImportToAccountAsync(
            account.AccountId, Stream(Csv(rows)), $"{account.Name}-{Guid.NewGuid():N}.csv", CancellationToken.None,
            choices: new ImportChoices { IgnoreRoutingCheck = true });
        result.Status.ShouldBe(PortfolioImportStatus.Success);
    }

    private static async Task<Portfolio> SeedPortfolioAsync(TestDbContext ctx)
    {
        var portfolio = new Portfolio("Test");
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio;
    }

    private static async Task<(Portfolio, Account Ira, Account Taxable)> SeedTwoAccountsAsync(TestDbContext ctx)
    {
        var portfolio = await SeedPortfolioAsync(ctx);
        var ira = new Account(portfolio.PortfolioId, "IRA", "vanguard.com", "AAA-111");
        var taxable = new Account(portfolio.PortfolioId, "Taxable", "vanguard.com", "BBB-222");
        ctx.Db.Accounts.AddRange(ira, taxable);
        await ctx.Db.SaveChangesAsync();
        return (portfolio, ira, taxable);
    }

    private static PortfolioImportService NewService(TestDbContext ctx, ImportRoutingOptions? routing = null)
    {
        var parsers = new IPortfolioFileParser[]
        {
            new QfxFileParser(),
            new VanguardTransactionHistoryReportParser(),
            new CsvLedgerTestParser(),
        };
        return new PortfolioImportService(
            ctx.Db, parsers, NoImpliedContributions.Instance, NullLogger<PortfolioImportService>.Instance,
            routing: routing);
    }

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));
}
