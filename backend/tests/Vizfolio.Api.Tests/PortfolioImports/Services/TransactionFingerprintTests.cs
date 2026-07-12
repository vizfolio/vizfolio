using Shouldly;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Services;

public sealed class TransactionFingerprintTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateOnly Date = new(2025, 1, 15);

    [Fact]
    public void Fingerprint_is_stable_across_the_parsed_and_persisted_representations()
    {
        var parsed = new ParsedTransaction(
            ExternalId: null, Type: TransactionType.Buy, TradeDate: Date, SettlementDate: null,
            Ticker: "voo", Cusip: null, Quantity: 10m, Price: 500m, Amount: -5000m,
            Fees: null, CurrencyCode: null, Memo: null);

        var entity = new AccountTransaction(Account, "QFX", "FIT-1", TransactionType.Buy, Date, -5000m);
        entity.SetSecurityReference("VOO", null);
        entity.SetTradeDetails(10m, 500m, null, null);

        TransactionFingerprint.Compute(Account, parsed).ShouldBe(TransactionFingerprint.Compute(entity));
    }

    [Fact]
    public void Buy_and_sell_of_the_same_magnitude_fingerprint_differently_via_sign()
    {
        var buy = TransactionFingerprint.Compute(Account, Date, "VOO", 10m, -5000m);
        var sell = TransactionFingerprint.Compute(Account, Date, "VOO", -10m, 5000m);

        buy.ShouldNotBe(sell);
    }

    [Fact]
    public void Amount_is_compared_at_cent_precision()
    {
        TransactionFingerprint.Compute(Account, Date, "VOO", 10m, -463.8200m)
            .ShouldBe(TransactionFingerprint.Compute(Account, Date, "VOO", 10m, -463.82m));

        TransactionFingerprint.Compute(Account, Date, "VOO", 10m, -463.82m)
            .ShouldNotBe(TransactionFingerprint.Compute(Account, Date, "VOO", 10m, -463.83m));
    }

    [Fact]
    public void Fingerprint_ignores_decimal_scale()
    {
        // 800, 800.00 and 800.0000 are the same amount — a database round-trip changes only the scale,
        // which must not change the fingerprint (the QFX-vs-Vanguard deposit dedup bug).
        var bare = TransactionFingerprint.Compute(Account, Date, null, null, 800m);
        var twoPlaces = TransactionFingerprint.Compute(Account, Date, null, null, 800.00m);
        var fourPlaces = TransactionFingerprint.Compute(Account, Date, null, null, 800.0000m);
        bare.ShouldBe(twoPlaces);
        twoPlaces.ShouldBe(fourPlaces);

        // Same for quantity scale.
        TransactionFingerprint.Compute(Account, Date, "VOO", 7m, -100m)
            .ShouldBe(TransactionFingerprint.Compute(Account, Date, "VOO", 7.0000m, -100m));
    }

    [Fact]
    public void Cash_rows_with_no_ticker_are_matched_consistently()
    {
        TransactionFingerprint.Compute(Account, Date, null, null, 200m)
            .ShouldBe(TransactionFingerprint.Compute(Account, Date, "", null, 200m));
    }

    [Fact]
    public void Different_accounts_never_collide()
    {
        TransactionFingerprint.Compute(Guid.NewGuid(), Date, "VOO", 10m, -5000m)
            .ShouldNotBe(TransactionFingerprint.Compute(Guid.NewGuid(), Date, "VOO", 10m, -5000m));
    }
}
