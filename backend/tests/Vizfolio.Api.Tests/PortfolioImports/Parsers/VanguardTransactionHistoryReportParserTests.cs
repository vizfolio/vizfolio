using System.Text;
using ClosedXML.Excel;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class VanguardTransactionHistoryReportParserTests
{
    private static readonly string[] Headers =
    [
        "Settlement date", "Trade date", "Symbol", "Name", "Type", "Account type",
        "Quantity", "Price", "Commission & fees**", "Amount",
    ];

    // Synthetic sample data. Column order matches Headers; null = blank cell.
    private static readonly string?[][] SampleRows =
    [
        ["3/15/2011", "3/12/2011", "VTWG", "VANGUARD RUSSELL 2000 GROWTH INDEX FD ETF SHS", "Buy", "CASH", "5.0000", "$71.4300", "Free", "-$357.1500"],
        ["8/9/2018", "8/8/2018", "VTSAX", "Vanguard Total Stock Market Index Fund Admiral Shares", "Buy (exchange)", "CASH", "2.3100", "$80.1000", null, "-$185.0300"],
        ["11/5/2014", null, "VMMXX", "Vanguard Cash Reserves Federal Money Market Fund", "Capital gain (ST)", null, "1.2500", null, null, "$1.2500"],
        ["6/20/2021", "6/20/2021", "VTIAX", "Vanguard Total International Stock Index Fund Admiral Shares", "Dividend", "CASH", null, null, null, "$512.4400"],
        ["2/7/2016", "2/7/2016", null, "CASH", "Fee", "CASH", null, null, null, "-$15.0000"],
        ["5/12/2017", "5/12/2017", null, "To: MY CREDIT UNION", "Funds Received", "CASH", null, null, null, "$250.0000"],
        ["3/30/2022", "3/30/2022", "VMFXX", "Vanguard Federal Money Market Fund (Settlement Fund)", "Reinvestment", "CASH", null, null, null, "-$7.8800"],
        ["8/9/2018", "8/8/2018", "VMMXX", "Vanguard Cash Reserves Federal Money Market Fund", "Sell (exchange)", "CASH", "-185.0300", "$1.0000", null, "$185.0300"],
        ["1/17/2023", "1/17/2023", "VMFXX", "Vanguard Federal Money Market Fund (Settlement Fund)", "Sweep in", "CASH", null, null, null, "-$420.7700"],
        ["1/25/2023", "1/25/2023", "VMFXX", "Vanguard Federal Money Market Fund (Settlement Fund)", "Sweep out", "CASH", null, null, null, "$455.1200"],
        ["9/3/2014", "9/3/2014", null, "CASH", "Transfer (incoming)", "CASH", null, null, null, "$275.5000"],
        ["9/1/2014", null, "VMMXX", "Vanguard Cash Reserves Federal Money Market Fund", "TRANSFER TO 987654", null, "-275.1000", null, null, "$275.1000"],
        ["4/22/2025", "4/22/2025", null, "To: MY CREDIT UNION", "Contribution", "CASH", null, null, null, "$150.0000"],
    ];

    [Fact]
    public async Task CanParseAsync_returns_true_for_a_vanguard_report_signature()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = BuildReport(SampleRows);

        (await parser.CanParseAsync(stream, "report.xlsx", CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task CanParseAsync_returns_false_for_an_unrelated_workbook()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "Foo";
        ws.Cell(1, 2).Value = "Bar";
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        (await parser.CanParseAsync(stream, "other.xlsx", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanParseAsync_returns_false_for_non_xlsx_content()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Settlement date,Trade date,Symbol,Amount\n"));

        (await parser.CanParseAsync(stream, "report.csv", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task ParseAsync_reads_every_row_and_stops_before_the_disclosures_footer()
    {
        var txs = await ParseSampleAsync();
        txs.Count.ShouldBe(SampleRows.Length);
    }

    [Fact]
    public async Task ParseAsync_maps_a_buy_with_negative_amount_positive_quantity_and_verbatim_source_type()
    {
        var buy = (await ParseSampleAsync())[0];

        buy.Type.ShouldBe(TransactionType.Buy);
        buy.SourceType.ShouldBe("Buy");
        buy.Ticker.ShouldBe("VTWG");
        buy.TradeDate.ShouldBe(new DateOnly(2011, 3, 12));
        buy.SettlementDate.ShouldBe(new DateOnly(2011, 3, 15));
        buy.Quantity.ShouldBe(5.0m);
        buy.Price.ShouldBe(71.43m);
        buy.Amount.ShouldBe(-357.15m);
        buy.Fees.ShouldBeNull(); // "Free"
        buy.Memo!.ShouldContain("RUSSELL");
    }

    [Fact]
    public async Task ParseAsync_falls_back_to_settlement_date_when_trade_date_is_blank()
    {
        var capitalGain = (await ParseSampleAsync())[2];

        capitalGain.Type.ShouldBe(TransactionType.CapitalGain);
        capitalGain.SourceType.ShouldBe("Capital gain (ST)");
        capitalGain.TradeDate.ShouldBe(new DateOnly(2014, 11, 5)); // fell back to settlement
        capitalGain.SettlementDate.ShouldBe(new DateOnly(2014, 11, 5));
        capitalGain.Amount.ShouldBe(1.25m);
    }

    [Fact]
    public async Task ParseAsync_maps_a_sell_with_negative_quantity_and_positive_amount()
    {
        var sell = (await ParseSampleAsync())[7];

        sell.Type.ShouldBe(TransactionType.Sell);
        sell.SourceType.ShouldBe("Sell (exchange)");
        sell.Quantity.ShouldBe(-185.03m);
        sell.Amount.ShouldBe(185.03m);
    }

    [Fact]
    public async Task ParseAsync_maps_cash_rows_without_a_symbol()
    {
        var txs = await ParseSampleAsync();

        var fee = txs[4];
        fee.Type.ShouldBe(TransactionType.Fee);
        fee.Ticker.ShouldBeNull();
        fee.Amount.ShouldBe(-15m);

        var fundsReceived = txs[5];
        fundsReceived.Type.ShouldBe(TransactionType.Deposit);
        fundsReceived.Amount.ShouldBe(250m);
    }

    [Fact]
    public async Task ParseAsync_maps_sweeps_to_Other_so_they_never_count_as_contributions()
    {
        var txs = await ParseSampleAsync();

        txs[8].Type.ShouldBe(TransactionType.Other); // Sweep in
        txs[8].SourceType.ShouldBe("Sweep in");
        txs[9].Type.ShouldBe(TransactionType.Other); // Sweep out
    }

    [Fact]
    public async Task ParseAsync_signs_external_transfers_by_direction_not_by_the_reported_amount()
    {
        var txs = await ParseSampleAsync();

        // Incoming transfer keeps its positive (money in) amount.
        var incoming = txs[10];
        incoming.Type.ShouldBe(TransactionType.Transfer);
        incoming.Amount.ShouldBe(275.50m);

        // Outgoing "TRANSFER TO" reports +275.10 (the money-market sale leg) but must be stored negative
        // so it reduces contributions. Trade date is blank -> falls back to settlement.
        var outgoing = txs[11];
        outgoing.Type.ShouldBe(TransactionType.Transfer);
        outgoing.SourceType.ShouldBe("TRANSFER TO 987654");
        outgoing.Amount.ShouldBe(-275.10m);
        outgoing.TradeDate.ShouldBe(new DateOnly(2014, 9, 1));
    }

    [Fact]
    public async Task ParseAsync_maps_an_ira_contribution_to_a_deposit()
    {
        var contribution = (await ParseSampleAsync())[12];

        contribution.Type.ShouldBe(TransactionType.Deposit); // counts toward contributions
        contribution.SourceType.ShouldBe("Contribution");    // IRA meaning preserved verbatim
        contribution.Amount.ShouldBe(150m);
    }

    private static async Task<IReadOnlyList<ParsedTransaction>> ParseSampleAsync()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = BuildReport(SampleRows);
        var parsed = await parser.ParseAsync(stream, "report.xlsx", CancellationToken.None);
        parsed.Statements.Count.ShouldBe(1);
        parsed.Statements[0].InstitutionCode.ShouldBeNull();
        return parsed.Statements[0].Transactions;
    }

    /// <summary>
    /// Builds a workbook shaped like the real report: a title block, the header on row 4, data rows,
    /// a blank separator, then a DISCLOSURES footer that must be ignored.
    /// </summary>
    private static MemoryStream BuildReport(string?[][] dataRows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Transactions");
        ws.Cell(1, 1).Value = "Transaction history";
        for (var c = 0; c < Headers.Length; c++) ws.Cell(4, c + 1).Value = Headers[c];

        var row = 5;
        foreach (var data in dataRows)
        {
            for (var c = 0; c < data.Length; c++)
                if (data[c] is { } value) ws.Cell(row, c + 1).Value = value;
            row++;
        }

        row++; // blank separator line
        ws.Cell(row, 1).Value = "DISCLOSURES";
        ws.Cell(row + 1, 1).Value = "Past performance is no guarantee of future results.";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
