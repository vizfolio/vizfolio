using System.Text;
using ClosedXML.Excel;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Parsers;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class VanguardTransactionHistoryReportParserTests
{
    [Fact]
    public async Task CanParseAsync_returns_true_for_xlsx_carrying_the_vanguard_signature_headers()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = WorkbookWithHeaderRow(VanguardTransactionHistoryReportParser.SignatureTokens);

        var result = await parser.CanParseAsync(stream, "report.xlsx", CancellationToken.None);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CanParseAsync_returns_false_for_xlsx_without_the_signature_headers()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = WorkbookWithHeaderRow(["Foo", "Bar", "Baz"]);

        var result = await parser.CanParseAsync(stream, "other.xlsx", CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task CanParseAsync_returns_false_for_non_xlsx_content()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Account Number,Trade Date,Symbol,Transaction Type\n"));

        var result = await parser.CanParseAsync(stream, "report.csv", CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ParseAsync_is_not_yet_implemented()
    {
        var parser = new VanguardTransactionHistoryReportParser();
        await using var stream = WorkbookWithHeaderRow(VanguardTransactionHistoryReportParser.SignatureTokens);

        await Should.ThrowAsync<NotSupportedException>(() =>
            parser.ParseAsync(stream, "report.xlsx", CancellationToken.None));
    }

    private static MemoryStream WorkbookWithHeaderRow(IReadOnlyList<string> headers)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Transactions");
        for (var i = 0; i < headers.Count; i++)
            worksheet.Cell(1, i + 1).Value = headers[i];

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
