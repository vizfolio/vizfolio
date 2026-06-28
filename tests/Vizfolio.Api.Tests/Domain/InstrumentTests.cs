using Shouldly;
using Vizfolio.Domain.Instruments;

namespace Vizfolio.Api.Tests.Domain;

public sealed class InstrumentTests
{
    [Fact]
    public void Constructor_assigns_id_kind_and_created_timestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var instrument = new Instrument(InstrumentKind.Security);

        instrument.InstrumentId.ShouldNotBe(Guid.Empty);
        instrument.Kind.ShouldBe(InstrumentKind.Security);
        instrument.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
        instrument.SecurityId.ShouldBeNull();
        instrument.FundId.ShouldBeNull();
        instrument.Symbol.ShouldBeNull();
    }

    [Theory]
    [InlineData(InstrumentKind.Crypto)]
    [InlineData(InstrumentKind.Cash)]
    [InlineData(InstrumentKind.Other)]
    public void Constructor_supports_kinds_with_no_underlying_reference(InstrumentKind kind)
    {
        var instrument = new Instrument(kind);

        instrument.Kind.ShouldBe(kind);
        instrument.SecurityId.ShouldBeNull();
        instrument.FundId.ShouldBeNull();
    }

    [Fact]
    public void SetIdentifiers_normalises_symbol_isin_and_cusip()
    {
        var instrument = new Instrument(InstrumentKind.Crypto);

        instrument.SetIdentifiers(" btc ", " Bitcoin ", " us0378331005 ", " 037833100 ");

        instrument.Symbol.ShouldBe("BTC");
        instrument.Name.ShouldBe("Bitcoin");
        instrument.Isin.ShouldBe("US0378331005");
        instrument.Cusip.ShouldBe("037833100");
    }

    [Fact]
    public void SetIdentifiers_treats_blank_as_null()
    {
        var instrument = new Instrument(InstrumentKind.Other);

        instrument.SetIdentifiers("   ", null, "", "  ");

        instrument.Symbol.ShouldBeNull();
        instrument.Name.ShouldBeNull();
        instrument.Isin.ShouldBeNull();
        instrument.Cusip.ShouldBeNull();
    }

    [Fact]
    public void SetClassification_uppercases_codes()
    {
        var instrument = new Instrument(InstrumentKind.Fund);

        instrument.SetClassification(" equity ", " domestic_equity ");

        instrument.AssetCategoryCode.ShouldBe("EQUITY");
        instrument.AssetClassCode.ShouldBe("DOMESTIC_EQUITY");
    }

    [Fact]
    public void SetCurrency_normalises_to_uppercase()
    {
        var instrument = new Instrument(InstrumentKind.Security);

        instrument.SetCurrency(" usd ");

        instrument.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public void LinkToSecurity_sets_security_id_for_security_kind()
    {
        var instrument = new Instrument(InstrumentKind.Security);
        var securityId = Guid.NewGuid();

        instrument.LinkToSecurity(securityId);

        instrument.SecurityId.ShouldBe(securityId);
    }

    [Fact]
    public void LinkToSecurity_rejects_empty_id()
    {
        var instrument = new Instrument(InstrumentKind.Security);

        Should.Throw<ArgumentException>(() => instrument.LinkToSecurity(Guid.Empty));
    }

    [Theory]
    [InlineData(InstrumentKind.Fund)]
    [InlineData(InstrumentKind.Crypto)]
    [InlineData(InstrumentKind.Cash)]
    [InlineData(InstrumentKind.Other)]
    public void LinkToSecurity_rejects_wrong_kind(InstrumentKind kind)
    {
        var instrument = new Instrument(kind);

        Should.Throw<InvalidOperationException>(() => instrument.LinkToSecurity(Guid.NewGuid()));
    }

    [Fact]
    public void LinkToFund_sets_fund_id_for_fund_kind()
    {
        var instrument = new Instrument(InstrumentKind.Fund);
        var fundId = Guid.NewGuid();

        instrument.LinkToFund(fundId);

        instrument.FundId.ShouldBe(fundId);
    }

    [Fact]
    public void LinkToFund_rejects_empty_id()
    {
        var instrument = new Instrument(InstrumentKind.Fund);

        Should.Throw<ArgumentException>(() => instrument.LinkToFund(Guid.Empty));
    }

    [Theory]
    [InlineData(InstrumentKind.Security)]
    [InlineData(InstrumentKind.Crypto)]
    [InlineData(InstrumentKind.Cash)]
    [InlineData(InstrumentKind.Other)]
    public void LinkToFund_rejects_wrong_kind(InstrumentKind kind)
    {
        var instrument = new Instrument(kind);

        Should.Throw<InvalidOperationException>(() => instrument.LinkToFund(Guid.NewGuid()));
    }
}
