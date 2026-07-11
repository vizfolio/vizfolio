using Shouldly;
using Vizfolio.Domain.Reference;

namespace Vizfolio.Api.Tests.Domain.Reference;

public sealed class AssetCategoryTests
{
    [Fact]
    public void Constructor_uppercases_code()
    {
        var category = new AssetCategory("ec", "Equity-Common", null);

        category.Code.ShouldBe("EC");
    }

    [Fact]
    public void Constructor_accepts_dashed_codes()
    {
        var category = new AssetCategory("ABS-MBS", "ABS-Mortgage Backed Security", null);

        category.Code.ShouldBe("ABS-MBS");
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONGCODE")]
    [InlineData("BAD CODE")]
    public void Constructor_rejects_invalid_code(string invalid)
    {
        Should.Throw<ArgumentException>(() => new AssetCategory(invalid, "Something", null));
    }

    [Fact]
    public void Constructor_rejects_blank_name()
    {
        Should.Throw<ArgumentException>(() => new AssetCategory("EC", " ", null));
    }
}
