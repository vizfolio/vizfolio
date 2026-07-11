using System.Text;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class CsvFileParserTests
{
    [Fact]
    public async Task CanParseAsync_returns_true_when_headers_include_canonical_set()
    {
        var parser = new CsvFileParser();
        await using var stream = StringStream(
            "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo\n" +
            "2026-06-01,Buy,VOO,1,100,-100,0,USD,note\n");

        var result = await parser.CanParseAsync(stream, "sample.csv", CancellationToken.None);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CanParseAsync_returns_false_when_canonical_headers_missing()
    {
        var parser = new CsvFileParser();
        await using var stream = StringStream("Foo,Bar\n1,2\n");

        var result = await parser.CanParseAsync(stream, "weird.csv", CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ParseAsync_reads_rows_and_normalises_fields()
    {
        var parser = new CsvFileParser();
        await using var stream = StringStream(
            "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo,ExternalId\n" +
            "2026-06-01,Buy,voo,2,100.50,-201.00,0,usd,\"hello, world\",ext-1\n" +
            "2026-06-02,Dividend,VOO,,,5.25,,,Q2 div,\n");

        var parsed = await parser.ParseAsync(stream, "sample.csv", CancellationToken.None);

        parsed.SourceSystem.ShouldBe("CSV");
        parsed.Statements.Count.ShouldBe(1);
        var statement = parsed.Statements[0];
        statement.InstitutionCode.ShouldBeNull();
        statement.AccountNumber.ShouldBeNull();
        statement.Transactions.Count.ShouldBe(2);

        var buy = statement.Transactions[0];
        buy.Type.ShouldBe(TransactionType.Buy);
        buy.TradeDate.ShouldBe(new DateOnly(2026, 6, 1));
        buy.Ticker.ShouldBe("voo");
        buy.Quantity.ShouldBe(2m);
        buy.Price.ShouldBe(100.50m);
        buy.Amount.ShouldBe(-201.00m);
        buy.CurrencyCode.ShouldBe("usd");
        buy.Memo.ShouldBe("hello, world");
        buy.ExternalId.ShouldBe("ext-1");

        var div = statement.Transactions[1];
        div.Type.ShouldBe(TransactionType.Dividend);
        div.Amount.ShouldBe(5.25m);
        div.ExternalId.ShouldBeNull();
        div.Quantity.ShouldBeNull();
    }

    [Fact]
    public async Task ParseAsync_rejects_unknown_transaction_type()
    {
        var parser = new CsvFileParser();
        await using var stream = StringStream(
            "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo\n" +
            "2026-06-01,Spaghetti,VOO,1,100,-100,0,USD,\n");

        await Should.ThrowAsync<InvalidDataException>(() =>
            parser.ParseAsync(stream, "sample.csv", CancellationToken.None));
    }

    private static MemoryStream StringStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));
}
