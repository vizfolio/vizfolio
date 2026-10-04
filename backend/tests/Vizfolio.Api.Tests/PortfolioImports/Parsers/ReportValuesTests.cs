using Shouldly;
using Vizfolio.Application.PortfolioImports.Parsers;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class ReportValuesTests
{
    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("-$357.15", -357.15)]
    [InlineData("($15.00)", -15.00)]
    [InlineData("0.12345", 0.12345)]
    public void ParseMoney_reads_broker_formatted_numbers(string raw, double expected)
        => ReportValues.ParseMoney(raw).ShouldBe((decimal)expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--")]
    [InlineData("Free")]
    [InlineData("n/a")]
    public void ParseMoney_is_null_for_placeholders_and_unreadable_text(string? raw)
        => ReportValues.ParseMoney(raw).ShouldBeNull();

    [Theory]
    [InlineData("Nov 3, 2014", 2014, 11, 3)]
    [InlineData("Aug 9, 2013", 2013, 8, 9)]
    [InlineData("3/15/2011", 2011, 3, 15)]
    [InlineData("2026-01-02", 2026, 1, 2)]
    public void ParseDate_reads_the_formats_brokers_print(string raw, int year, int month, int day)
        => ReportValues.ParseDate(raw).ShouldBe(new DateOnly(year, month, day));

    [Theory]
    [InlineData("--")]
    [InlineData("not a date")]
    public void ParseDate_is_null_for_placeholders_and_unreadable_text(string raw)
        => ReportValues.ParseDate(raw).ShouldBeNull();
}
