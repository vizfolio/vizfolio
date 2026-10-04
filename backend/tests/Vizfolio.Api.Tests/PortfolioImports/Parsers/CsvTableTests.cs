using System.Text;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Parsers;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class CsvTableTests
{
    private static CsvTable Read(string content) => CsvTable.Read(new MemoryStream(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void Quoted_fields_keep_their_commas_escaped_quotes_and_line_breaks()
    {
        var table = Read("A,B,C\r\n\"Nov 17, 2025\",\"say \"\"hi\"\"\",\"two\nlines\"\r\nx,y,z\r\n");

        table.Headers.ShouldBe(new[] { "A", "B", "C" });
        table.Rows.Count.ShouldBe(2);
        table.Rows[0].Fields.ShouldBe(new[] { "Nov 17, 2025", "say \"hi\"", "two\nlines" });
        table.Rows[1].LineNumber.ShouldBe(4); // the quoted line break spans lines 2–3
    }

    [Fact]
    public void Short_rows_are_padded_and_blank_lines_skipped()
    {
        var table = Read("A,B,C\n1,2\n\n   \n4,5,6\n");

        table.Rows.Count.ShouldBe(2);
        table.Rows[0].Fields.ShouldBe(new[] { "1", "2", "" });
        table.Rows[1].LineNumber.ShouldBe(5);
    }

    [Fact]
    public void Columns_are_found_by_header_name_regardless_of_case_or_padding()
    {
        var table = Read(" Posted Date ,Amount\n\"Jan 1, 2026\",$5\n");

        table.Column("posted date").ShouldBe(0);
        table.Column("AMOUNT").ShouldBe(1);
        table.Column("Units").ShouldBeNull();
        CsvTable.Field(table.Rows[0], table.Column("Units")).ShouldBe(string.Empty);
        table.HasColumns(["Posted Date", "Amount"]).ShouldBeTrue();
    }

    [Fact]
    public void ReadHeaders_skips_a_byte_order_mark_and_leading_blank_lines()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("\r\nA,B\n1,2\n")).ToArray();

        CsvTable.ReadHeaders(new MemoryStream(bytes)).ShouldBe(new[] { "A", "B" });
    }

    [Fact]
    public void An_empty_file_has_no_headers_or_rows()
    {
        var table = Read("");

        table.Headers.ShouldBeEmpty();
        table.Rows.ShouldBeEmpty();
    }
}
