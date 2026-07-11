using Shouldly;
using Vizfolio.Domain.Reference;

namespace Vizfolio.Api.Tests.Domain.Reference;

public sealed class CurrencyTests
{
    [Fact]
    public void Constructor_uppercases_code()
    {
        var currency = new Currency("usd", "US Dollar", 2, "$");

        currency.Code.ShouldBe("USD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDA")]
    [InlineData("US1")]
    public void Constructor_rejects_invalid_code(string invalid)
    {
        Should.Throw<ArgumentException>(() => new Currency(invalid, "Something", 2, null));
    }

    [Fact]
    public void Constructor_rejects_blank_name()
    {
        Should.Throw<ArgumentException>(() => new Currency("USD", "  ", 2, null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Constructor_rejects_invalid_minor_unit(int invalid)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Currency("USD", "US Dollar", invalid, null));
    }

    [Fact]
    public void Constructor_normalizes_blank_symbol_to_null()
    {
        var currency = new Currency("USD", "US Dollar", 2, "   ");

        currency.Symbol.ShouldBeNull();
    }
}
