using Shouldly;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Domain;

public sealed class FundHoldingTests
{
    private static readonly Guid SampleSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Constructor_requires_fund_snapshot_id()
    {
        Should.Throw<ArgumentException>(() => new FundHolding(Guid.Empty, 0.05m));
    }

    [Fact]
    public void LinkToSecurity_rejects_empty_id()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        Should.Throw<ArgumentException>(() => holding.LinkToSecurity(Guid.Empty));
    }

    [Fact]
    public void LinkToSecurity_assigns_security_id()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);
        var securityId = Guid.NewGuid();

        holding.LinkToSecurity(securityId);

        holding.SecurityId.ShouldBe(securityId);
    }

    [Fact]
    public void SetIdentifiers_uppercases_ticker_and_isin()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetIdentifiers("Apple Inc.", "aapl", "us0378331005");

        holding.Name.ShouldBe("Apple Inc.");
        holding.Ticker.ShouldBe("AAPL");
        holding.Isin.ShouldBe("US0378331005");
    }

    [Fact]
    public void SetIdentifiers_normalizes_blank_ticker_to_null()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetIdentifiers("Apple Inc.", "   ", null);

        holding.Ticker.ShouldBeNull();
        holding.Isin.ShouldBeNull();
    }

    [Fact]
    public void SetValuation_assigns_money_and_units()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetValuation(123_456.78m, 1_000m, "NS");

        holding.FairValueUsd.ShouldBe(123_456.78m);
        holding.Balance.ShouldBe(1_000m);
        holding.Units.ShouldBe("NS");
    }

    [Fact]
    public void SetClassification_assigns_categorization()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetClassification("EC", "US", "USD");

        holding.AssetCategoryCode.ShouldBe("EC");
        holding.CountryCode.ShouldBe("US");
        holding.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public void SetClassification_uppercases_and_trims_codes()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetClassification("  ec ", " us ", "usd ");

        holding.AssetCategoryCode.ShouldBe("EC");
        holding.CountryCode.ShouldBe("US");
        holding.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public void SetClassification_normalizes_blank_codes_to_null()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetClassification("   ", "", null);

        holding.AssetCategoryCode.ShouldBeNull();
        holding.CountryCode.ShouldBeNull();
        holding.CurrencyCode.ShouldBeNull();
    }

    [Fact]
    public void SetIssuerCik_zero_pads_to_ten_digits()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetIssuerCik("19617");

        holding.IssuerCik.ShouldBe("0000019617");
    }

    [Fact]
    public void SetIssuerCik_accepts_already_padded_value()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        holding.SetIssuerCik("0000019617");

        holding.IssuerCik.ShouldBe("0000019617");
    }

    [Fact]
    public void SetIssuerCik_blank_clears_value()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);
        holding.SetIssuerCik("19617");

        holding.SetIssuerCik("   ");

        holding.IssuerCik.ShouldBeNull();
    }

    [Fact]
    public void SetIssuerCik_null_clears_value()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);
        holding.SetIssuerCik("19617");

        holding.SetIssuerCik(null);

        holding.IssuerCik.ShouldBeNull();
    }

    [Fact]
    public void SetIssuerCik_rejects_non_digit_input()
    {
        var holding = new FundHolding(SampleSnapshotId, 0.05m);

        Should.Throw<ArgumentException>(() => holding.SetIssuerCik("AAPL"));
    }
}
