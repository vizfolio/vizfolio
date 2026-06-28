using Shouldly;
using Vizfolio.Domain.Reference;

namespace Vizfolio.Api.Tests.Domain.Reference;

public sealed class AssetClassTests
{
    [Fact]
    public void Constructor_uppercases_code()
    {
        var cls = new AssetClass("equity", "Equity", null);

        cls.Code.ShouldBe("EQUITY");
    }

    [Fact]
    public void Constructor_allows_underscore_in_code()
    {
        var cls = new AssetClass("real_estate", "Real Estate", null);

        cls.Code.ShouldBe("REAL_ESTATE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("EQUITY-FUND")]
    [InlineData("WAY-TOO-LONG-FOR-A-CODE")]
    [InlineData("E1")]
    public void Constructor_rejects_invalid_code(string invalid)
    {
        Should.Throw<ArgumentException>(() => new AssetClass(invalid, "Something", null));
    }

    [Fact]
    public void Constructor_rejects_blank_name()
    {
        Should.Throw<ArgumentException>(() => new AssetClass("EQUITY", "  ", null));
    }
}
