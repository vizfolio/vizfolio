using Shouldly;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Domain;

public sealed class AccountTransactionTests
{
    [Fact]
    public void Constructor_initialises_required_fields_and_normalises_source_system()
    {
        var accountId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var tx = new AccountTransaction(
            accountId,
            sourceSystem: "qfx",
            externalId: "FITID-1",
            type: TransactionType.Buy,
            tradeDate: new DateOnly(2026, 6, 1),
            amount: -1234.56m);

        tx.AccountTransactionId.ShouldNotBe(Guid.Empty);
        tx.AccountId.ShouldBe(accountId);
        tx.SourceSystem.ShouldBe("QFX");
        tx.ExternalId.ShouldBe("FITID-1");
        tx.Type.ShouldBe(TransactionType.Buy);
        tx.TradeDate.ShouldBe(new DateOnly(2026, 6, 1));
        tx.Amount.ShouldBe(-1234.56m);
        tx.ImportedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void Constructor_rejects_empty_account_id()
    {
        Should.Throw<ArgumentException>(() =>
            new AccountTransaction(Guid.Empty, "QFX", "x", TransactionType.Buy, new DateOnly(2026, 1, 1), 0m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_blank_external_id(string? externalId)
    {
        Should.Throw<ArgumentException>(() =>
            new AccountTransaction(Guid.NewGuid(), "QFX", externalId!, TransactionType.Buy, new DateOnly(2026, 1, 1), 0m));
    }

    [Fact]
    public void SetSecurityReference_uppercases_ticker_and_cusip()
    {
        var tx = NewTx();

        tx.SetSecurityReference(" voo ", "037833100 ");

        tx.Ticker.ShouldBe("VOO");
        tx.Cusip.ShouldBe("037833100");
    }

    [Fact]
    public void SetSecurityReference_treats_blank_as_null()
    {
        var tx = NewTx();

        tx.SetSecurityReference("   ", null);

        tx.Ticker.ShouldBeNull();
        tx.Cusip.ShouldBeNull();
    }

    [Fact]
    public void AccountHoldingId_is_null_by_default()
    {
        var tx = NewTx();

        tx.AccountHoldingId.ShouldBeNull();
    }

    [Fact]
    public void LinkToHolding_assigns_id()
    {
        var tx = NewTx();
        var holdingId = Guid.NewGuid();

        tx.LinkToHolding(holdingId);

        tx.AccountHoldingId.ShouldBe(holdingId);
    }

    [Fact]
    public void LinkToHolding_rejects_empty_id()
    {
        var tx = NewTx();

        Should.Throw<ArgumentException>(() => tx.LinkToHolding(Guid.Empty));
    }

    [Fact]
    public void SetTradeDetails_assigns_quantity_price_fees_settlement()
    {
        var tx = NewTx();

        tx.SetTradeDetails(10m, 100m, 1.5m, new DateOnly(2026, 6, 3));

        tx.Quantity.ShouldBe(10m);
        tx.Price.ShouldBe(100m);
        tx.Fees.ShouldBe(1.5m);
        tx.SettlementDate.ShouldBe(new DateOnly(2026, 6, 3));
    }

    [Fact]
    public void SetCurrency_normalises_to_uppercase()
    {
        var tx = NewTx();

        tx.SetCurrency(" usd ");

        tx.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public void SetMemo_trims_and_treats_blank_as_null()
    {
        var tx = NewTx();

        tx.SetMemo("  hello  ");
        tx.Memo.ShouldBe("hello");

        tx.SetMemo("   ");
        tx.Memo.ShouldBeNull();
    }

    private static AccountTransaction NewTx() => new(
        Guid.NewGuid(),
        "QFX",
        Guid.NewGuid().ToString("N"),
        TransactionType.Buy,
        new DateOnly(2026, 1, 1),
        0m);
}
