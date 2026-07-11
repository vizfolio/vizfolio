using Shouldly;
using Vizfolio.Domain.Reference;

namespace Vizfolio.Api.Tests.Domain.Reference;

public sealed class CountryTests
{
    [Fact]
    public void Constructor_uppercases_code()
    {
        var country = new Country("us", "United States", "Americas");

        country.Code.ShouldBe("US");
    }

    [Theory]
    [InlineData("")]
    [InlineData("USA")]
    [InlineData("U")]
    [InlineData("U1")]
    public void Constructor_rejects_invalid_code(string invalid)
    {
        Should.Throw<ArgumentException>(() => new Country(invalid, "Somewhere", null));
    }

    [Fact]
    public void Constructor_rejects_blank_name()
    {
        Should.Throw<ArgumentException>(() => new Country("US", "  ", null));
    }

    [Fact]
    public void Constructor_normalizes_blank_region_to_null()
    {
        var country = new Country("US", "United States", "   ");

        country.Region.ShouldBeNull();
    }
}
