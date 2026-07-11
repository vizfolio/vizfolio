using Shouldly;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Domain;

public sealed class ShareClassTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_requires_class_id(string invalid)
    {
        Should.Throw<ArgumentException>(() => new ShareClass(invalid, null, null, null));
    }

    [Fact]
    public void Constructor_rejects_negative_expense_ratio()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ShareClass("C000001", "Class A", "ACMEX", -0.001m));
    }

    [Fact]
    public void Constructor_uppercases_ticker()
    {
        var shareClass = new ShareClass("C000001", "Class A", " acmex ", 0.0050m);

        shareClass.Ticker.ShouldBe("ACMEX");
    }

    [Fact]
    public void Constructor_allows_null_optional_fields()
    {
        var shareClass = new ShareClass("C000001", null, null, null);

        shareClass.Name.ShouldBeNull();
        shareClass.Ticker.ShouldBeNull();
        shareClass.ExpenseRatio.ShouldBeNull();
    }
}
