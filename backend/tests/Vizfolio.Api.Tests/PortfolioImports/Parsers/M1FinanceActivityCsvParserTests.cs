using System.Text;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class M1FinanceActivityCsvParserTests
{
    private const string Header =
        "Date,Posted Date,Symbol,Description,Transaction Type,Amount,Units,Unit Type,Unit Price,Security Id,Security Id Type";

    // Synthetic rows shaped like a real M1 export: quoted dates with commas, "--" for empty cells, unsigned amounts,
    // and a last row that drops its trailing empty column.
    private const string SampleCsv =
        Header + "\n" +
        "\"Feb 11, 2019\",\"Feb 11, 2019\",,ACH deposit of $250 completed.,TRANSFER,$250.00,--,CURRENCY,--,,\n" +
        "\"Feb 11, 2019\",\"Feb 11, 2019\",ZXTS,2.5 shares of ZXTS purchased.,PURCHASED,$100.00,2.5,SHARES,$40.00,TEST00001,CUSIP\n" +
        "\"Jun 8, 2019\",\"Jun 7, 2019\",ZXBD,Dividend of TEST00002 $12.34 received.,DIVIDEND,$12.34,--,CURRENCY,--,TEST00002,CUSIP\n" +
        "\"Jan 14, 2019\",\"Jan 13, 2019\",,PROMO_CREDIT_INITIAL_FUNDING,CASH,$25.00,--,CURRENCY,--,";

    private static Task<ParsedPortfolioFile> ParseAsync(string csv)
        => new M1FinanceActivityCsvParser().ParseAsync(Stream(csv), "activity.csv", CancellationToken.None);

    private static async Task<IReadOnlyList<ParsedTransaction>> ParseRowsAsync(params string[] rows)
        => (await ParseAsync(Header + "\n" + string.Join("\n", rows))).Statements.Single().Transactions;

    private static MemoryStream Stream(string content, bool withBom = false)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new MemoryStream(withBom ? [.. Encoding.UTF8.GetPreamble(), .. bytes] : bytes);
    }

    // ---------------- detection ----------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanParseAsync_recognises_the_m1_activity_header_with_or_without_a_byte_order_mark(bool withBom)
    {
        var parser = new M1FinanceActivityCsvParser();

        (await parser.CanParseAsync(Stream(SampleCsv, withBom), "activity.csv", CancellationToken.None)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Date,Symbol,Amount\n2025-01-01,ZXTS,10")]
    [InlineData("Settlement date,Trade date,Symbol,Name,Type,Quantity,Price,Commission & fees,Amount\n")]
    [InlineData("")]
    public async Task CanParseAsync_rejects_other_csv_files(string content)
    {
        var parser = new M1FinanceActivityCsvParser();

        (await parser.CanParseAsync(Stream(content), "other.csv", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanParseAsync_rejects_binary_content()
    {
        var parser = new M1FinanceActivityCsvParser();
        var bytes = new byte[64 * 1024];
        new Random(42).NextBytes(bytes);

        (await parser.CanParseAsync(new MemoryStream(bytes), "report.xlsx", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public void Advertises_the_m1_source_system_and_csv_extension()
    {
        var parser = new M1FinanceActivityCsvParser();

        parser.SourceSystem.ShouldBe("M1");
        parser.FileExtensions.ShouldBe(new[] { ".csv" });
        M1FinanceActivityCsvParser.SignatureTokens.ShouldContain("Posted Date");
    }

    // ---------------- the statement ----------------

    [Fact]
    public async Task ParseAsync_returns_one_accountless_m1_statement_without_positions()
    {
        var file = await ParseAsync(SampleCsv);

        file.SourceSystem.ShouldBe("M1");
        var statement = file.Statements.ShouldHaveSingleItem();
        statement.InstitutionCode.ShouldBe("m1.com");
        statement.AccountNumber.ShouldBeNull();
        statement.Positions.ShouldBeEmpty();
        statement.Transactions.Count.ShouldBe(4);
        file.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ParseAsync_throws_when_the_file_has_no_m1_header()
    {
        await Should.ThrowAsync<InvalidDataException>(() => ParseAsync("Foo,Bar\n1,2"));
    }

    // ---------------- row mapping ----------------

    [Fact]
    public async Task An_ACH_deposit_transfer_is_a_positive_Deposit_with_no_security()
    {
        var deposit = (await ParseAsync(SampleCsv)).Statements[0].Transactions[0];

        deposit.Type.ShouldBe(TransactionType.Deposit);
        deposit.Amount.ShouldBe(250m);
        deposit.Ticker.ShouldBeNull();
        deposit.Cusip.ShouldBeNull();
        deposit.Quantity.ShouldBeNull();
        deposit.Price.ShouldBeNull();
        deposit.TradeDate.ShouldBe(new DateOnly(2019, 2, 11));
        deposit.SourceType.ShouldBe("TRANSFER");
        deposit.Memo.ShouldBe("ACH deposit of $250 completed.");
        deposit.ExternalId.ShouldBeNull();
    }

    [Fact]
    public async Task A_purchase_is_a_Buy_that_spends_cash_for_fractional_shares()
    {
        var buy = (await ParseAsync(SampleCsv)).Statements[0].Transactions[1];

        buy.Type.ShouldBe(TransactionType.Buy);
        buy.Amount.ShouldBe(-100m);
        buy.Quantity.ShouldBe(2.5m);
        buy.Price.ShouldBe(40m);
        buy.Ticker.ShouldBe("ZXTS");
        buy.Cusip.ShouldBe("TEST00001");
        buy.SourceType.ShouldBe("PURCHASED");
        buy.IsSettlementFund.ShouldBeFalse();
    }

    [Fact]
    public async Task A_dividend_is_cash_income_and_keeps_the_posted_date_as_settlement()
    {
        var dividend = (await ParseAsync(SampleCsv)).Statements[0].Transactions[2];

        dividend.Type.ShouldBe(TransactionType.Dividend);
        dividend.Amount.ShouldBe(12.34m);
        dividend.Quantity.ShouldBeNull(); // "--" units: paid in cash, not reinvested at source
        dividend.Ticker.ShouldBe("ZXBD");
        dividend.Cusip.ShouldBe("TEST00002");
        dividend.TradeDate.ShouldBe(new DateOnly(2019, 6, 8));
        dividend.SettlementDate.ShouldBe(new DateOnly(2019, 6, 7));
    }

    [Fact]
    public async Task A_promo_credit_is_income_not_a_contribution_and_survives_the_short_last_row()
    {
        var promo = (await ParseAsync(SampleCsv)).Statements[0].Transactions[3];

        promo.Type.ShouldBe(TransactionType.Interest);
        promo.Amount.ShouldBe(25m);
        promo.SourceType.ShouldBe("CASH");
        promo.Memo.ShouldBe("PROMO_CREDIT_INITIAL_FUNDING");
        promo.TradeDate.ShouldBe(new DateOnly(2019, 1, 14));
        promo.Cusip.ShouldBeNull();
    }

    [Fact]
    public async Task A_sale_is_a_Sell_that_removes_shares_and_brings_cash_in()
    {
        var sell = (await ParseRowsAsync(
            "\"Mar 2, 2026\",\"Mar 3, 2026\",ZXTS,1.5 shares of ZXTS sold.,SOLD,$60.00,1.5,SHARES,$40.00,TEST00001,CUSIP"))[0];

        sell.Type.ShouldBe(TransactionType.Sell);
        sell.Quantity.ShouldBe(-1.5m);
        sell.Amount.ShouldBe(60m);
    }

    [Theory]
    [InlineData("ACH withdrawal of $200 completed.,TRANSFER,$200.00")]
    [InlineData("Transfer completed.,TRANSFER,-$200.00")]
    public async Task A_withdrawal_transfer_is_a_negative_Withdrawal(string descriptionTypeAmount)
    {
        var withdrawal = (await ParseRowsAsync(
            $"\"Jan 5, 2026\",\"Jan 5, 2026\",,{descriptionTypeAmount},--,CURRENCY,--,,"))[0];

        withdrawal.Type.ShouldBe(TransactionType.Withdrawal);
        withdrawal.Amount.ShouldBe(-200m);
    }

    [Fact]
    public async Task Interest_and_fees_are_signed_as_income_and_a_charge()
    {
        var rows = await ParseRowsAsync(
            "\"Jan 31, 2026\",\"Jan 31, 2026\",,Interest paid.,INTEREST,$1.25,--,CURRENCY,--,,",
            "\"Feb 1, 2026\",\"Feb 1, 2026\",,Membership fee.,FEE,$3.00,--,CURRENCY,--,,");

        rows[0].Type.ShouldBe(TransactionType.Interest);
        rows[0].Amount.ShouldBe(1.25m);
        rows[1].Type.ShouldBe(TransactionType.Fee);
        rows[1].Amount.ShouldBe(-3m);
    }

    [Fact]
    public async Task An_unknown_label_is_imported_as_Other_with_a_warning_that_names_it()
    {
        var file = await ParseAsync(Header + "\n" +
            "\"Jun 1, 2026\",\"Jun 1, 2026\",ZXTS,10 shares of ZXTS transferred in.,TRANSFER,$400.00,10,SHARES,--,TEST00001,CUSIP\n" +
            "\"Jun 2, 2026\",\"Jun 2, 2026\",,Something new.,MYSTERY,$5.00,--,CURRENCY,--,,");

        var rows = file.Statements[0].Transactions;
        rows.ShouldAllBe(t => t.Type == TransactionType.Other);
        rows[0].Quantity.ShouldBe(10m);
        rows[0].Amount.ShouldBe(400m);

        file.Warnings.Count.ShouldBe(2);
        file.Warnings.ShouldAllBe(w => w.Code == ImportWarningCodes.UnmappedLabel);
        file.Warnings[0].Message.ShouldContain("\"TRANSFER\" (shares)");
        file.Warnings[0].Samples.ShouldHaveSingleItem().ShouldBe("line 2: 10 shares of ZXTS transferred in.");
        file.Warnings[1].Message.ShouldContain("\"MYSTERY\"");
    }

    [Fact]
    public async Task A_row_without_a_readable_date_is_skipped_with_a_warning_and_the_rest_import()
    {
        var file = await ParseAsync(Header + "\n" +
            "not a date,--,,ACH deposit of $5 completed.,TRANSFER,$5.00,--,CURRENCY,--,,\n" +
            "\"Jan 5, 2026\",\"Jan 5, 2026\",,ACH deposit of $7 completed.,TRANSFER,$7.00,--,CURRENCY,--,,");

        file.Statements[0].Transactions.ShouldHaveSingleItem().Amount.ShouldBe(7m);
        var warning = file.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(ImportWarningCodes.RowFailed);
        warning.Samples.ShouldBe(new[] { "line 2" });
    }

    [Fact]
    public async Task A_security_known_only_by_an_unsupported_id_type_warns()
    {
        var file = await ParseAsync(Header + "\n" +
            "\"Jan 5, 2026\",\"Jan 5, 2026\",,Dividend received.,DIVIDEND,$1.00,--,CURRENCY,--,XX0000000001,ISIN");

        file.Statements[0].Transactions[0].Cusip.ShouldBeNull();
        file.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.UnsupportedSecurityId);
    }
}
