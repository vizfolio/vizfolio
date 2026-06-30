using Shouldly;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Domain;

public sealed class AccountHoldingTests
{
    [Fact]
    public void Constructor_assigns_id_account_kind_and_created_timestamp()
    {
        var accountId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var holding = new AccountHolding(accountId, AccountHoldingKind.Security);

        holding.AccountHoldingId.ShouldNotBe(Guid.Empty);
        holding.AccountId.ShouldBe(accountId);
        holding.Kind.ShouldBe(AccountHoldingKind.Security);
        holding.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
        holding.SecurityId.ShouldBeNull();
        holding.FundId.ShouldBeNull();
        holding.Symbol.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_empty_account_id()
    {
        Should.Throw<ArgumentException>(() =>
            new AccountHolding(Guid.Empty, AccountHoldingKind.Security));
    }

    [Theory]
    [InlineData(AccountHoldingKind.Crypto)]
    [InlineData(AccountHoldingKind.Cash)]
    [InlineData(AccountHoldingKind.Other)]
    public void Constructor_supports_kinds_with_no_underlying_reference(AccountHoldingKind kind)
    {
        var holding = new AccountHolding(Guid.NewGuid(), kind);

        holding.Kind.ShouldBe(kind);
        holding.SecurityId.ShouldBeNull();
        holding.FundId.ShouldBeNull();
    }

    [Fact]
    public void SetIdentifiers_normalises_symbol_isin_and_cusip()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Crypto);

        holding.SetIdentifiers(" btc ", " Bitcoin ", " us0378331005 ", " 037833100 ");

        holding.Symbol.ShouldBe("BTC");
        holding.Name.ShouldBe("Bitcoin");
        holding.Isin.ShouldBe("US0378331005");
        holding.Cusip.ShouldBe("037833100");
    }

    [Fact]
    public void SetIdentifiers_treats_blank_as_null()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Other);

        holding.SetIdentifiers("   ", null, "", "  ");

        holding.Symbol.ShouldBeNull();
        holding.Name.ShouldBeNull();
        holding.Isin.ShouldBeNull();
        holding.Cusip.ShouldBeNull();
    }

    [Fact]
    public void SetClassification_uppercases_codes()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Fund);

        holding.SetClassification(" equity ", " domestic_equity ");

        holding.AssetCategoryCode.ShouldBe("EQUITY");
        holding.AssetClassCode.ShouldBe("DOMESTIC_EQUITY");
    }

    [Fact]
    public void SetCurrency_normalises_to_uppercase()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Security);

        holding.SetCurrency(" usd ");

        holding.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public void LinkToSecurity_sets_security_id_for_security_kind()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Security);
        var securityId = Guid.NewGuid();

        holding.LinkToSecurity(securityId);

        holding.SecurityId.ShouldBe(securityId);
    }

    [Fact]
    public void LinkToSecurity_rejects_empty_id()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Security);

        Should.Throw<ArgumentException>(() => holding.LinkToSecurity(Guid.Empty));
    }

    [Theory]
    [InlineData(AccountHoldingKind.Fund)]
    [InlineData(AccountHoldingKind.Crypto)]
    [InlineData(AccountHoldingKind.Cash)]
    [InlineData(AccountHoldingKind.Other)]
    public void LinkToSecurity_rejects_wrong_kind(AccountHoldingKind kind)
    {
        var holding = new AccountHolding(Guid.NewGuid(), kind);

        Should.Throw<InvalidOperationException>(() => holding.LinkToSecurity(Guid.NewGuid()));
    }

    [Fact]
    public void LinkToFund_sets_fund_id_for_fund_kind()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Fund);
        var fundId = Guid.NewGuid();

        holding.LinkToFund(fundId);

        holding.FundId.ShouldBe(fundId);
    }

    [Fact]
    public void LinkToFund_rejects_empty_id()
    {
        var holding = new AccountHolding(Guid.NewGuid(), AccountHoldingKind.Fund);

        Should.Throw<ArgumentException>(() => holding.LinkToFund(Guid.Empty));
    }

    [Theory]
    [InlineData(AccountHoldingKind.Security)]
    [InlineData(AccountHoldingKind.Crypto)]
    [InlineData(AccountHoldingKind.Cash)]
    [InlineData(AccountHoldingKind.Other)]
    public void LinkToFund_rejects_wrong_kind(AccountHoldingKind kind)
    {
        var holding = new AccountHolding(Guid.NewGuid(), kind);

        Should.Throw<InvalidOperationException>(() => holding.LinkToFund(Guid.NewGuid()));
    }
}
