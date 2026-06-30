using Shouldly;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Api.Tests.Domain;

public sealed class SecurityTests
{
    private static readonly DateTimeOffset SampleFetchedAt = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_zero_pads_cik_to_10_digits()
    {
        var security = new Security("320193", SampleFetchedAt);

        security.Cik.ShouldBe("0000320193");
    }

    [Fact]
    public void Constructor_preserves_already_padded_cik()
    {
        var security = new Security("0000320193", SampleFetchedAt);

        security.Cik.ShouldBe("0000320193");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_empty_cik(string invalid)
    {
        Should.Throw<ArgumentException>(() => new Security(invalid, SampleFetchedAt));
    }

    [Theory]
    [InlineData("AAPL")]
    [InlineData("32-0193")]
    [InlineData("320193X")]
    public void Constructor_rejects_non_numeric_cik(string invalid)
    {
        Should.Throw<ArgumentException>(() => new Security(invalid, SampleFetchedAt));
    }

    [Fact]
    public void SetTickers_uppercases_trims_and_deduplicates()
    {
        var security = new Security("320193", SampleFetchedAt);

        security.SetTickers(["aapl", " AAPL ", "msft", ""]);

        security.Tickers.ShouldBe(["AAPL", "MSFT"]);
    }

    [Fact]
    public void SetExchanges_uppercases_trims_and_deduplicates()
    {
        var security = new Security("320193", SampleFetchedAt);

        security.SetExchanges([" Nasdaq ", "NASDAQ", "nyse"]);

        security.Exchanges.ShouldBe(["NASDAQ", "NYSE"]);
    }

    [Fact]
    public void UpdateProfile_assigns_descriptive_fields()
    {
        var security = new Security("320193", SampleFetchedAt);

        security.UpdateProfile("Apple Inc.", "10-K filer", "Technology", "Electronic Computers", "US");

        security.Name.ShouldBe("Apple Inc.");
        security.EntityType.ShouldBe("10-K filer");
        security.Sector.ShouldBe("Technology");
        security.Industry.ShouldBe("Electronic Computers");
        security.CountryCode.ShouldBe("US");
    }

    [Fact]
    public void UpdateProfile_uppercases_and_trims_country_code()
    {
        var security = new Security("320193", SampleFetchedAt);

        security.UpdateProfile(null, null, null, null, "  us ");

        security.CountryCode.ShouldBe("US");
    }

    [Fact]
    public void UpdateProfile_normalizes_blank_country_to_null()
    {
        var security = new Security("320193", SampleFetchedAt);
        security.UpdateProfile(null, null, null, null, "US");

        security.UpdateProfile(null, null, null, null, "   ");

        security.CountryCode.ShouldBeNull();
    }

    [Fact]
    public void MarkRefreshed_rejects_older_timestamps()
    {
        var security = new Security("320193", SampleFetchedAt);

        Should.Throw<InvalidOperationException>(() => security.MarkRefreshed(SampleFetchedAt.AddDays(-1)));
    }

    [Fact]
    public void MarkRefreshed_advances_fetch_timestamp()
    {
        var security = new Security("320193", SampleFetchedAt);
        var later = SampleFetchedAt.AddDays(7);

        security.MarkRefreshed(later);

        security.EdgarFetchedAt.ShouldBe(later);
    }
}
