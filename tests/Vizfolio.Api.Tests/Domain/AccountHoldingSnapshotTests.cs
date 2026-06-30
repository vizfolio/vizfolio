using Shouldly;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Domain;

public sealed class AccountHoldingSnapshotTests
{
    [Fact]
    public void Constructor_assigns_id_holding_quantity_source_and_timestamp()
    {
        var holdingId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 6, 1);
        var before = DateTimeOffset.UtcNow;

        var snapshot = new AccountHoldingSnapshot(
            holdingId, asOf, 12.5m, AccountHoldingSnapshotSource.BrokerPosition);

        snapshot.AccountHoldingSnapshotId.ShouldNotBe(Guid.Empty);
        snapshot.AccountHoldingId.ShouldBe(holdingId);
        snapshot.AsOf.ShouldBe(asOf);
        snapshot.Quantity.ShouldBe(12.5m);
        snapshot.Source.ShouldBe(AccountHoldingSnapshotSource.BrokerPosition);
        snapshot.RecordedAt.ShouldBeGreaterThanOrEqualTo(before);
        snapshot.CostBasis.ShouldBeNull();
        snapshot.MarketValue.ShouldBeNull();
        snapshot.UnitPrice.ShouldBeNull();
        snapshot.CurrencyCode.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_empty_holding_id()
    {
        Should.Throw<ArgumentException>(() => new AccountHoldingSnapshot(
            Guid.Empty,
            new DateOnly(2026, 6, 1),
            1m,
            AccountHoldingSnapshotSource.OpeningBalance));
    }

    [Fact]
    public void Constructor_accepts_zero_units_to_record_closed_position()
    {
        var snapshot = new AccountHoldingSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            0m,
            AccountHoldingSnapshotSource.BrokerPosition);

        snapshot.Quantity.ShouldBe(0m);
    }

    [Fact]
    public void SetValuation_assigns_amounts_and_normalises_currency()
    {
        var snapshot = new AccountHoldingSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            10m,
            AccountHoldingSnapshotSource.Statement);

        snapshot.SetValuation(costBasis: 1500m, marketValue: 2000m, unitPrice: 200m, currencyCode: " usd ");

        snapshot.CostBasis.ShouldBe(1500m);
        snapshot.MarketValue.ShouldBe(2000m);
        snapshot.UnitPrice.ShouldBe(200m);
        snapshot.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public void SetValuation_treats_blank_currency_as_null()
    {
        var snapshot = new AccountHoldingSnapshot(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            10m,
            AccountHoldingSnapshotSource.Statement);

        snapshot.SetValuation(null, null, null, "   ");

        snapshot.CurrencyCode.ShouldBeNull();
    }
}
