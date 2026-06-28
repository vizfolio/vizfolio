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

        holding.AssetCategory.ShouldBe("EC");
        holding.Country.ShouldBe("US");
        holding.Currency.ShouldBe("USD");
    }
}
