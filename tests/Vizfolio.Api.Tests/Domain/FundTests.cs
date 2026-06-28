using Shouldly;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Domain;

public sealed class FundTests
{
    [Fact]
    public void Constructor_assigns_id_and_trims_series_id()
    {
        var fund = new Fund("  S000012345 ");

        fund.Id.ShouldNotBe(Guid.Empty);
        fund.SeriesId.ShouldBe("S000012345");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_empty_series_id(string invalid)
    {
        Should.Throw<ArgumentException>(() => new Fund(invalid));
    }

    [Fact]
    public void UpdateProfile_normalizes_empty_registrant_cik_to_null()
    {
        var fund = new Fund("S000012345");

        fund.UpdateProfile("Acme Income Fund", "   ", "Acme Funds Inc.");

        fund.RegistrantCik.ShouldBeNull();
    }

    [Fact]
    public void UpdateProfile_assigns_descriptive_fields()
    {
        var fund = new Fund("S000012345");

        fund.UpdateProfile("Acme Income Fund", "0000123456", "Acme Funds Inc.");

        fund.Name.ShouldBe("Acme Income Fund");
        fund.RegistrantCik.ShouldBe("0000123456");
        fund.RegistrantName.ShouldBe("Acme Funds Inc.");
    }
}
