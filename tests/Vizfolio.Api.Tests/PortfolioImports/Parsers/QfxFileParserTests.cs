using System.Text;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

public sealed class QfxFileParserTests
{
    private const string Ofx2Sample = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <SIGNONMSGSRSV1><SONRS><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS><DTSERVER>20260601120000</DTSERVER><LANGUAGE>ENG</LANGUAGE>
    <FI><ORG>Fidelity</ORG><FID>7776</FID></FI>
  </SONRS></SIGNONMSGSRSV1>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>fidelity.com</BROKERID><ACCTID>X1234</ACCTID></INVACCTFROM>
      <INVTRANLIST>
        <DTSTART>20260101000000</DTSTART><DTEND>20260601120000</DTEND>
        <BUYSTOCK>
          <INVBUY>
            <INVTRAN><FITID>BUY-1</FITID><DTTRADE>20260115</DTTRADE><DTSETTLE>20260117</DTSETTLE><MEMO>Buy VOO</MEMO></INVTRAN>
            <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <UNITS>2</UNITS><UNITPRICE>500.00</UNITPRICE><COMMISSION>0.99</COMMISSION><TOTAL>-1000.99</TOTAL>
            <CURRENCY><CURSYM>USD</CURSYM><CURRATE>1</CURRATE></CURRENCY>
          </INVBUY>
          <BUYTYPE>BUY</BUYTYPE>
        </BUYSTOCK>
        <INCOME>
          <INVTRAN><FITID>DIV-1</FITID><DTTRADE>20260315</DTTRADE><MEMO>Q1 div</MEMO></INVTRAN>
          <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
          <INCOMETYPE>DIV</INCOMETYPE>
          <TOTAL>3.50</TOTAL>
          <CURRENCY><CURSYM>USD</CURSYM><CURRATE>1</CURRATE></CURRENCY>
        </INCOME>
        <REINVEST>
          <INVTRAN><FITID>REINV-1</FITID><DTTRADE>20260316</DTTRADE></INVTRAN>
          <SECID><UNIQUEID>VOO</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
          <INCOMETYPE>DIV</INCOMETYPE>
          <UNITS>0.007</UNITS><UNITPRICE>500.00</UNITPRICE><TOTAL>-3.50</TOTAL>
        </REINVEST>
      </INVTRANLIST>
    </INVSTMTRS>
  </INVSTMTTRNRS></INVSTMTMSGSRSV1>
</OFX>
""";

    private const string Ofx1Sample = """
OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII
CHARSET:1252
COMPRESSION:NONE
OLDFILEUID:NONE
NEWFILEUID:NONE

<OFX>
<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20260601<LANGUAGE>ENG<FI><ORG>Schwab<FID>1234</FI></SONRS></SIGNONMSGSRSV1>
<BANKMSGSRSV1><STMTTRNRS><TRNUID>1<STATUS><CODE>0<SEVERITY>INFO</STATUS>
<STMTRS><CURDEF>USD<BANKACCTFROM><BANKID>0<ACCTID>SC-9988<ACCTTYPE>SAVINGS</BANKACCTFROM>
<BANKTRANLIST><DTSTART>20260101<DTEND>20260601
<STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260201<TRNAMT>250.00<FITID>DEP-1<NAME>Deposit<MEMO>External transfer</STMTTRN>
<STMTTRN><TRNTYPE>FEE<DTPOSTED>20260301<TRNAMT>-5.00<FITID>FEE-1<NAME>Account fee<MEMO>Monthly fee</STMTTRN>
</BANKTRANLIST>
</STMTRS></STMTTRNRS></BANKMSGSRSV1>
</OFX>
""";

    [Fact]
    public async Task CanParseAsync_recognises_ofx2_xml()
    {
        var parser = new QfxFileParser();
        await using var stream = StringStream(Ofx2Sample);

        var result = await parser.CanParseAsync(stream, "sample.qfx", CancellationToken.None);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CanParseAsync_recognises_ofx1_sgml_header()
    {
        var parser = new QfxFileParser();
        await using var stream = StringStream(Ofx1Sample);

        var result = await parser.CanParseAsync(stream, "sample.qfx", CancellationToken.None);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CanParseAsync_returns_false_for_non_ofx()
    {
        var parser = new QfxFileParser();
        await using var stream = StringStream("not an ofx file at all\n");

        var result = await parser.CanParseAsync(stream, "sample.txt", CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ParseAsync_extracts_buy_income_and_reinvest_from_ofx2()
    {
        var parser = new QfxFileParser();
        await using var stream = StringStream(Ofx2Sample);

        var parsed = await parser.ParseAsync(stream, "sample.qfx", CancellationToken.None);

        parsed.SourceSystem.ShouldBe("QFX");
        parsed.SourceInstitution.ShouldBe("Fidelity");
        parsed.SourceAccountNumber.ShouldBe("X1234");
        parsed.Transactions.Count.ShouldBe(3);

        var buy = parsed.Transactions[0];
        buy.ExternalId.ShouldBe("BUY-1");
        buy.Type.ShouldBe(TransactionType.Buy);
        buy.TradeDate.ShouldBe(new DateOnly(2026, 1, 15));
        buy.SettlementDate.ShouldBe(new DateOnly(2026, 1, 17));
        buy.Ticker.ShouldBe("VOO");
        buy.Quantity.ShouldBe(2m);
        buy.Price.ShouldBe(500.00m);
        buy.Amount.ShouldBe(-1000.99m);
        buy.Fees.ShouldBe(0.99m);
        buy.CurrencyCode.ShouldBe("USD");

        var div = parsed.Transactions[1];
        div.ExternalId.ShouldBe("DIV-1");
        div.Type.ShouldBe(TransactionType.Dividend);
        div.Ticker.ShouldBe("VOO");
        div.Amount.ShouldBe(3.50m);

        var reinv = parsed.Transactions[2];
        reinv.ExternalId.ShouldBe("REINV-1");
        reinv.Type.ShouldBe(TransactionType.Reinvest);
        reinv.Quantity.ShouldBe(0.007m);
        reinv.Amount.ShouldBe(-3.50m);
    }

    [Fact]
    public async Task ParseAsync_resolves_cusip_to_ticker_via_seclist_in_ofx2()
    {
        const string sample = """
<?xml version="1.0" encoding="UTF-8"?>
<?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
<OFX>
  <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
    <INVSTMTRS>
      <DTASOF>20260601120000</DTASOF>
      <CURDEF>USD</CURDEF>
      <INVACCTFROM><BROKERID>vanguard.com</BROKERID><ACCTID>X1234</ACCTID></INVACCTFROM>
      <INVTRANLIST>
        <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
        <BUYMF>
          <INVBUY>
            <INVTRAN><FITID>BUY-CUSIP-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>TEST00001</UNIQUEID><UNIQUEIDTYPE>CUSIP</UNIQUEIDTYPE></SECID>
            <UNITS>1</UNITS><UNITPRICE>10.00</UNITPRICE><TOTAL>-10.00</TOTAL>
          </INVBUY>
          <BUYTYPE>BUY</BUYTYPE>
        </BUYMF>
        <BUYMF>
          <INVBUY>
            <INVTRAN><FITID>BUY-CUSIP-2</FITID><DTTRADE>20260120</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>TEST00099</UNIQUEID><UNIQUEIDTYPE>CUSIP</UNIQUEIDTYPE></SECID>
            <UNITS>1</UNITS><UNITPRICE>5.00</UNITPRICE><TOTAL>-5.00</TOTAL>
          </INVBUY>
          <BUYTYPE>BUY</BUYTYPE>
        </BUYMF>
      </INVTRANLIST>
    </INVSTMTRS>
  </INVSTMTTRNRS></INVSTMTMSGSRSV1>
  <SECLISTMSGSRSV1><SECLIST>
    <MFINFO>
      <SECINFO>
        <SECID><UNIQUEID>TEST00001</UNIQUEID><UNIQUEIDTYPE>CUSIP</UNIQUEIDTYPE></SECID>
        <SECNAME>Test Fund One</SECNAME>
        <TICKER>TST1</TICKER>
      </SECINFO>
      <MFTYPE>OPENEND</MFTYPE>
    </MFINFO>
  </SECLIST></SECLISTMSGSRSV1>
</OFX>
""";
        var parser = new QfxFileParser();
        await using var stream = StringStream(sample);

        var parsed = await parser.ParseAsync(stream, "sample.qfx", CancellationToken.None);

        parsed.Transactions.Count.ShouldBe(2);

        var resolved = parsed.Transactions[0];
        resolved.Ticker.ShouldBe("TST1");
        resolved.Cusip.ShouldBe("TEST00001");

        var unmapped = parsed.Transactions[1];
        unmapped.Ticker.ShouldBeNull();
        unmapped.Cusip.ShouldBe("TEST00099");
    }

    [Fact]
    public async Task ParseAsync_resolves_cusip_to_ticker_via_seclist_in_ofx1_sgml()
    {
        const string sample = """
OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII
CHARSET:1252
COMPRESSION:NONE
OLDFILEUID:NONE
NEWFILEUID:NONE

<OFX>
<INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1<STATUS><CODE>0<SEVERITY>INFO</STATUS>
<INVSTMTRS><DTASOF>20260601<CURDEF>USD<INVACCTFROM><BROKERID>vanguard.com<ACCTID>X9999</INVACCTFROM>
<INVTRANLIST><DTSTART>20260101<DTEND>20260601
<BUYMF><INVBUY><INVTRAN><FITID>BUY-SGML-1<DTTRADE>20260115</INVTRAN><SECID><UNIQUEID>TEST00042<UNIQUEIDTYPE>CUSIP</SECID><UNITS>2<UNITPRICE>50.00<TOTAL>-100.00</INVBUY><BUYTYPE>BUY</BUYMF>
</INVTRANLIST></INVSTMTRS></INVSTMTTRNRS></INVSTMTMSGSRSV1>
<SECLISTMSGSRSV1><SECLIST>
<MFINFO><SECINFO><SECID><UNIQUEID>TEST00042<UNIQUEIDTYPE>CUSIP</SECID><SECNAME>Test Fund Forty Two<TICKER>TST42</SECINFO><MFTYPE>OPENEND</MFINFO>
</SECLIST></SECLISTMSGSRSV1>
</OFX>
""";
        var parser = new QfxFileParser();
        await using var stream = StringStream(sample);

        var parsed = await parser.ParseAsync(stream, "sample.qfx", CancellationToken.None);

        parsed.Transactions.Count.ShouldBe(1);
        parsed.Transactions[0].Ticker.ShouldBe("TST42");
        parsed.Transactions[0].Cusip.ShouldBe("TEST00042");
    }

    [Fact]
    public async Task ParseAsync_extracts_bank_transactions_from_ofx1_sgml()
    {
        var parser = new QfxFileParser();
        await using var stream = StringStream(Ofx1Sample);

        var parsed = await parser.ParseAsync(stream, "sample.qfx", CancellationToken.None);

        parsed.SourceSystem.ShouldBe("QFX");
        parsed.SourceInstitution.ShouldBe("Schwab");
        parsed.SourceAccountNumber.ShouldBe("SC-9988");
        parsed.Transactions.Count.ShouldBe(2);

        var deposit = parsed.Transactions[0];
        deposit.ExternalId.ShouldBe("DEP-1");
        deposit.Type.ShouldBe(TransactionType.Deposit);
        deposit.TradeDate.ShouldBe(new DateOnly(2026, 2, 1));
        deposit.Amount.ShouldBe(250.00m);
        deposit.Memo.ShouldBe("External transfer");

        var fee = parsed.Transactions[1];
        fee.ExternalId.ShouldBe("FEE-1");
        fee.Type.ShouldBe(TransactionType.Fee);
        fee.Amount.ShouldBe(-5.00m);
    }

    private static MemoryStream StringStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));
}
