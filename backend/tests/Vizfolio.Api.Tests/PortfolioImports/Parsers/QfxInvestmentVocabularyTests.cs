using System.Text;
using Shouldly;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Parsers;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Parsers;

/// <summary>
/// The whole OFX investment vocabulary (roadmap Phase 2.2): every aggregate becomes a ledger row or a warning, never
/// silence; one bad row never aborts the file; statement balances, short positions, price dates and broker
/// conventions (profiles) are read. One fixture per aggregate, built from synthetic values.
/// </summary>
public sealed class QfxInvestmentVocabularyTests
{
    private static readonly QfxFileParser Parser = new();

    // ---------------- transaction aggregates ----------------

    [Fact]
    public async Task RETOFCAP_is_return_of_capital_cash_in_tied_to_its_security()
    {
        var file = await ParseAsync(Statement("""
            <RETOFCAP>
              <INVTRAN><FITID>ROC-1</FITID><DTTRADE>20260210</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <TOTAL>12.34</TOTAL><SUBACCTSEC>CASH</SUBACCTSEC><SUBACCTFUND>CASH</SUBACCTFUND>
            </RETOFCAP>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Type.ShouldBe(TransactionType.ReturnOfCapital);
        row.Ticker.ShouldBe("XYZ");
        row.Amount.ShouldBe(12.34m);
        row.Quantity.ShouldBeNull();
        row.SubAccount.ShouldBe("CASH");
        file.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task SPLIT_keeps_the_ratio_the_change_in_shares_and_cash_in_lieu()
    {
        var file = await ParseAsync(Statement("""
            <SPLIT>
              <INVTRAN><FITID>SPL-1</FITID><DTTRADE>20260301</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <SUBACCTSEC>CASH</SUBACCTSEC>
              <OLDUNITS>10</OLDUNITS><NEWUNITS>30</NEWUNITS>
              <NUMERATOR>3</NUMERATOR><DENOMINATOR>1</DENOMINATOR>
              <FRACCASH>1.25</FRACCASH>
            </SPLIT>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Type.ShouldBe(TransactionType.Split);
        row.SplitNumerator.ShouldBe(3m);
        row.SplitDenominator.ShouldBe(1m);
        row.Quantity.ShouldBe(20m);
        row.Amount.ShouldBe(1.25m);
        file.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_SPLIT_without_a_ratio_is_kept_with_its_change_in_shares_and_a_warning()
    {
        var file = await ParseAsync(Statement("""
            <SPLIT>
              <INVTRAN><FITID>SPL-2</FITID><DTTRADE>20260301</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <OLDUNITS>10</OLDUNITS><NEWUNITS>20</NEWUNITS>
            </SPLIT>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.SplitNumerator.ShouldBeNull();
        row.Quantity.ShouldBe(10m);
        file.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.SplitWithoutRatio);
    }

    [Fact]
    public async Task JRNLSEC_and_JRNLFUND_are_journals_between_sub_accounts()
    {
        var file = await ParseAsync(Statement("""
            <JRNLSEC>
              <INVTRAN><FITID>J-1</FITID><DTTRADE>20260401</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <SUBACCTFROM>CASH</SUBACCTFROM><SUBACCTTO>MARGIN</SUBACCTTO><TFERACTION>OUT</TFERACTION><UNITS>5</UNITS>
            </JRNLSEC>
            <JRNLFUND>
              <INVTRAN><FITID>J-2</FITID><DTTRADE>20260401</DTTRADE></INVTRAN>
              <SUBACCTFROM>MARGIN</SUBACCTFROM><SUBACCTTO>CASH</SUBACCTTO><TOTAL>100.00</TOTAL>
            </JRNLFUND>
            """));

        var rows = file.Statements[0].Transactions;
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.Type == TransactionType.Journal);
        rows[0].Ticker.ShouldBe("XYZ");
        rows[0].SubAccount.ShouldBe("CASH→MARGIN");
        rows[1].Ticker.ShouldBeNull();
        rows[1].Amount.ShouldBe(100m);
        rows[1].SubAccount.ShouldBe("MARGIN→CASH");
    }

    [Fact]
    public async Task MARGININTEREST_is_interest_paid_and_INVEXPENSE_is_a_fee()
    {
        var file = await ParseAsync(Statement("""
            <MARGININTEREST>
              <INVTRAN><FITID>MI-1</FITID><DTTRADE>20260430</DTTRADE></INVTRAN>
              <TOTAL>4.10</TOTAL><SUBACCTFUND>MARGIN</SUBACCTFUND>
            </MARGININTEREST>
            <INVEXPENSE>
              <INVTRAN><FITID>EX-1</FITID><DTTRADE>20260430</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <TOTAL>2.00</TOTAL>
            </INVEXPENSE>
            """));

        var rows = file.Statements[0].Transactions;
        rows[0].Type.ShouldBe(TransactionType.Interest);
        rows[0].Amount.ShouldBe(-4.10m);
        rows[1].Type.ShouldBe(TransactionType.Fee);
        rows[1].Amount.ShouldBe(-2m);
        rows[1].Ticker.ShouldBe("XYZ");
    }

    [Fact]
    public async Task BUYDEBT_and_SELLDEBT_are_buys_and_sells_labelled_as_bonds()
    {
        var file = await ParseAsync(Statement("""
            <BUYDEBT>
              <INVBUY>
                <INVTRAN><FITID>BD-1</FITID><DTTRADE>20260105</DTTRADE></INVTRAN>
                <SECID><UNIQUEID>123456AB1</UNIQUEID><UNIQUEIDTYPE>CUSIP</UNIQUEIDTYPE></SECID>
                <UNITS>10</UNITS><UNITPRICE>99.5</UNITPRICE><TOTAL>-995.00</TOTAL>
              </INVBUY>
              <ACCRDINT>-3.00</ACCRDINT>
            </BUYDEBT>
            <SELLDEBT>
              <INVSELL>
                <INVTRAN><FITID>SD-1</FITID><DTTRADE>20260205</DTTRADE></INVTRAN>
                <SECID><UNIQUEID>123456AB1</UNIQUEID><UNIQUEIDTYPE>CUSIP</UNIQUEIDTYPE></SECID>
                <UNITS>-10</UNITS><UNITPRICE>100</UNITPRICE><TOTAL>1000.00</TOTAL>
              </INVSELL>
              <SELLREASON>SELL</SELLREASON>
            </SELLDEBT>
            """));

        var rows = file.Statements[0].Transactions;
        rows[0].Type.ShouldBe(TransactionType.Buy);
        rows[0].Cusip.ShouldBe("123456AB1");
        rows[0].SourceType.ShouldBe("BUYDEBT");
        rows[1].Type.ShouldBe(TransactionType.Sell);
        rows[1].SourceType.ShouldBe("SELLDEBT");
        file.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Option_trades_are_kept_as_buys_and_sells_with_a_warning_that_they_cant_be_priced()
    {
        var file = await ParseAsync(Statement("""
            <BUYOPT>
              <INVBUY>
                <INVTRAN><FITID>O-1</FITID><DTTRADE>20260105</DTTRADE></INVTRAN>
                <SECID><UNIQUEID>XYZ260620C00100000</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
                <UNITS>1</UNITS><UNITPRICE>2.5</UNITPRICE><TOTAL>-250.00</TOTAL>
              </INVBUY>
              <OPTBUYTYPE>BUYTOOPEN</OPTBUYTYPE><SHPERCTRCT>100</SHPERCTRCT>
            </BUYOPT>
            <SELLOPT>
              <INVSELL>
                <INVTRAN><FITID>O-2</FITID><DTTRADE>20260110</DTTRADE></INVTRAN>
                <SECID><UNIQUEID>XYZ260620C00100000</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
                <UNITS>-1</UNITS><UNITPRICE>3</UNITPRICE><TOTAL>300.00</TOTAL>
              </INVSELL>
              <OPTSELLTYPE>SELLTOCLOSE</OPTSELLTYPE><SHPERCTRCT>100</SHPERCTRCT>
            </SELLOPT>
            """));

        var rows = file.Statements[0].Transactions;
        rows.Select(r => r.Type).ShouldBe([TransactionType.Buy, TransactionType.Sell]);
        rows[0].SourceType.ShouldBe("BUYOPT BUYTOOPEN");
        var warning = file.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(ImportWarningCodes.OptionActivity);
        warning.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CLOSUREOPT_is_recorded_as_other_without_moving_shares_and_warns()
    {
        var file = await ParseAsync(Statement("""
            <CLOSUREOPT>
              <INVTRAN><FITID>C-1</FITID><DTTRADE>20260620</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ260620C00100000</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <OPTACTION>EXPIRE</OPTACTION><UNITS>1</UNITS><SHPERCTRCT>100</SHPERCTRCT><SUBACCTSEC>CASH</SUBACCTSEC>
            </CLOSUREOPT>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Type.ShouldBe(TransactionType.Other);
        row.Quantity.ShouldBeNull();
        row.SourceType.ShouldBe("CLOSUREOPT EXPIRE");
        file.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.OptionActivity);
    }

    [Fact]
    public async Task INCOME_MISC_is_imported_as_other_with_an_unmapped_label_warning()
    {
        var file = await ParseAsync(Statement("""
            <INCOME>
              <INVTRAN><FITID>M-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <INCOMETYPE>MISC</INCOMETYPE><TOTAL>7.00</TOTAL>
            </INCOME>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Type.ShouldBe(TransactionType.Other);
        row.Amount.ShouldBe(7m);
        row.SourceType.ShouldBe("INCOME MISC");
        var warning = file.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(ImportWarningCodes.UnmappedLabel);
        warning.Samples.ShouldBe(["INCOME M-1"]);
    }

    [Fact]
    public async Task INCOME_is_stored_gross_with_tax_withheld_as_its_own_fee_row()
    {
        var file = await ParseAsync(Statement("""
            <INCOME>
              <INVTRAN><FITID>D-9</FITID><DTTRADE>20260320</DTTRADE><DTSETTLE>20260322</DTSETTLE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <INCOMETYPE>DIV</INCOMETYPE><TOTAL>100.00</TOTAL><WITHHOLDING>15.00</WITHHOLDING><TAXES>1.00</TAXES>
            </INCOME>
            """));

        var rows = file.Statements[0].Transactions;
        rows.Count.ShouldBe(2);
        rows[0].Type.ShouldBe(TransactionType.Dividend);
        rows[0].Amount.ShouldBe(100m);
        rows[0].SettlementDate.ShouldBe(new DateOnly(2026, 3, 22));
        rows[1].Type.ShouldBe(TransactionType.Fee);
        rows[1].ExternalId.ShouldBe("D-9:wh");
        rows[1].Amount.ShouldBe(-16m);
        rows[1].SourceType.ShouldBe("Tax withheld");
        rows[1].Ticker.ShouldBe("XYZ");
    }

    [Fact]
    public async Task An_unknown_aggregate_is_a_warning_with_a_count_never_silently_dropped()
    {
        var file = await ParseAsync(Statement("""
            <FUTUREAGGREGATE><INVTRAN><FITID>F-1</FITID><DTTRADE>20260101</DTTRADE></INVTRAN></FUTUREAGGREGATE>
            <FUTUREAGGREGATE><INVTRAN><FITID>F-2</FITID><DTTRADE>20260102</DTTRADE></INVTRAN></FUTUREAGGREGATE>
            """));

        file.Statements[0].Transactions.ShouldBeEmpty();
        var warning = file.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(ImportWarningCodes.UnknownAggregate);
        warning.Count.ShouldBe(2);
        warning.Samples.ShouldBe(["FUTUREAGGREGATE F-1", "FUTUREAGGREGATE F-2"]);
    }

    [Fact]
    public async Task A_row_with_a_bad_date_fails_alone_and_the_rest_of_the_file_imports()
    {
        var file = await ParseAsync(Statement("""
            <INCOME>
              <INVTRAN><FITID>BAD-1</FITID><DTTRADE>2026-13-45</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <INCOMETYPE>DIV</INCOMETYPE><TOTAL>1.00</TOTAL>
            </INCOME>
            <INCOME>
              <INVTRAN><FITID>OK-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <INCOMETYPE>DIV</INCOMETYPE><TOTAL>2.00</TOTAL>
            </INCOME>
            """));

        file.Statements[0].Transactions.ShouldHaveSingleItem().ExternalId.ShouldBe("OK-1");
        var warning = file.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(ImportWarningCodes.RowFailed);
        warning.Samples.ShouldBe(["INCOME BAD-1"]);
    }

    [Fact]
    public async Task A_TRANSFER_reads_its_unit_price_when_present()
    {
        var file = await ParseAsync(Statement("""
            <TRANSFER>
              <INVTRAN><FITID>T-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <SUBACCTSEC>CASH</SUBACCTSEC><UNITS>4</UNITS><TFERACTION>IN</TFERACTION><POSTYPE>LONG</POSTYPE>
              <AVGCOSTBASIS>50</AVGCOSTBASIS><UNITPRICE>61.5</UNITPRICE>
            </TRANSFER>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Quantity.ShouldBe(4m);
        row.Price.ShouldBe(61.5m);
        row.Amount.ShouldBe(0m);
    }

    // ---------------- security ids ----------------

    [Fact]
    public async Task A_US_ISIN_yields_its_CUSIP_and_the_SECLIST_ticker()
    {
        var file = await ParseAsync(Statement("""
            <INCOME>
              <INVTRAN><FITID>I-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>US1234567890</UNIQUEID><UNIQUEIDTYPE>ISIN</UNIQUEIDTYPE></SECID>
              <INCOMETYPE>DIV</INCOMETYPE><TOTAL>2.00</TOTAL>
            </INCOME>
            """, secList: """
            <MFINFO><SECINFO><SECID><UNIQUEID>US1234567890</UNIQUEID><UNIQUEIDTYPE>ISIN</UNIQUEIDTYPE></SECID>
              <SECNAME>Example Fund</SECNAME><TICKER>EXMPX</TICKER></SECINFO></MFINFO>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Ticker.ShouldBe("EXMPX");
        row.Cusip.ShouldBe("123456789");
    }

    [Fact]
    public async Task An_unsupported_id_type_is_never_mistaken_for_a_ticker()
    {
        var file = await ParseAsync(Statement("""
            <INCOME>
              <INVTRAN><FITID>S-1</FITID><DTTRADE>20260115</DTTRADE></INVTRAN>
              <SECID><UNIQUEID>B0YBKJ7</UNIQUEID><UNIQUEIDTYPE>SEDOL</UNIQUEIDTYPE></SECID>
              <INCOMETYPE>DIV</INCOMETYPE><TOTAL>2.00</TOTAL>
            </INCOME>
            """));

        var row = file.Statements[0].Transactions.ShouldHaveSingleItem();
        row.Ticker.ShouldBeNull();
        row.Cusip.ShouldBeNull();
        file.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.UnsupportedSecurityId);
    }

    // ---------------- positions and balances ----------------

    [Fact]
    public async Task Positions_keep_their_price_date_and_short_positions_are_negative()
    {
        var file = await ParseAsync(Statement(transactions: "", positions: """
            <POSSTOCK><INVPOS>
              <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <HELDINACCT>CASH</HELDINACCT><POSTYPE>LONG</POSTYPE><UNITS>10</UNITS><UNITPRICE>20</UNITPRICE><MKTVAL>200</MKTVAL>
              <DTPRICEASOF>20260529</DTPRICEASOF>
            </INVPOS></POSSTOCK>
            <POSSTOCK><INVPOS>
              <SECID><UNIQUEID>SHRT</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <HELDINACCT>MARGIN</HELDINACCT><POSTYPE>SHORT</POSTYPE><UNITS>5</UNITS><UNITPRICE>10</UNITPRICE><MKTVAL>50</MKTVAL>
              <DTPRICEASOF>20260529</DTPRICEASOF>
            </INVPOS></POSSTOCK>
            """));

        var positions = file.Statements[0].Positions;
        positions[0].PriceAsOf.ShouldBe(new DateOnly(2026, 5, 29));
        positions[0].Units.ShouldBe(10m);
        positions[1].Units.ShouldBe(-5m);
        positions[1].MarketValue.ShouldBe(-50m);
    }

    [Fact]
    public async Task INVBAL_gives_the_statements_available_cash()
    {
        var file = await ParseAsync(Statement(transactions: "", balances: """
            <INVBAL><AVAILCASH>321.09</AVAILCASH><MARGINBALANCE>0</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>
            """));

        var cash = file.Statements[0].Cash.ShouldNotBeNull();
        cash.AsOf.ShouldBe(new DateOnly(2026, 6, 1));
        cash.AvailableCash.ShouldBe(321.09m);
        cash.IncludesSettlementFund.ShouldBeFalse();
        file.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_margin_balance_is_read_and_warned_about()
    {
        var file = await ParseAsync(Statement(transactions: "", balances: """
            <INVBAL><AVAILCASH>10</AVAILCASH><MARGINBALANCE>-500</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>
            """));

        file.Statements[0].Cash!.MarginBalance.ShouldBe(-500m);
        file.Warnings.ShouldHaveSingleItem().Code.ShouldBe(ImportWarningCodes.MarginBalance);
    }

    [Fact]
    public async Task Without_a_profile_the_position_worth_exactly_the_available_cash_is_the_settlement_fund()
    {
        var file = await ParseAsync(Statement(transactions: "", positions: """
            <POSMF><INVPOS>
              <SECID><UNIQUEID>CORE</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <HELDINACCT>CASH</HELDINACCT><POSTYPE>LONG</POSTYPE><UNITS>150.5</UNITS><UNITPRICE>1</UNITPRICE><MKTVAL>150.50</MKTVAL>
            </INVPOS></POSMF>
            <POSMF><INVPOS>
              <SECID><UNIQUEID>FUND</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <HELDINACCT>CASH</HELDINACCT><POSTYPE>LONG</POSTYPE><UNITS>3</UNITS><UNITPRICE>100</UNITPRICE><MKTVAL>300</MKTVAL>
            </INVPOS></POSMF>
            """, balances: "<INVBAL><AVAILCASH>150.50</AVAILCASH><MARGINBALANCE>0</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>"));

        var statement = file.Statements[0];
        statement.Positions.Single(p => p.Ticker == "CORE").IsSettlementFund.ShouldBeTrue();
        statement.Positions.Single(p => p.Ticker == "FUND").IsSettlementFund.ShouldBeFalse();
        statement.Cash!.IncludesSettlementFund.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_profile_available_cash_matching_no_position_is_cash_of_its_own()
    {
        var file = await ParseAsync(Statement(transactions: "", positions: """
            <POSMF><INVPOS>
              <SECID><UNIQUEID>FUND</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <HELDINACCT>CASH</HELDINACCT><POSTYPE>LONG</POSTYPE><UNITS>3</UNITS><UNITPRICE>100</UNITPRICE><MKTVAL>300</MKTVAL>
            </INVPOS></POSMF>
            """, balances: "<INVBAL><AVAILCASH>42</AVAILCASH><MARGINBALANCE>0</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>"));

        file.Statements[0].Positions.ShouldAllBe(p => !p.IsSettlementFund);
        file.Statements[0].Cash!.IncludesSettlementFund.ShouldBeFalse();
    }

    [Fact]
    public async Task Vanguard_sweeps_and_its_settlement_fund_position_are_marked_from_the_broker_profile()
    {
        // As in Vanguard's own exports: sweeps say "MONEY FUND PURCHASE/REDEMPTION", and AVAILCASH is the
        // settlement fund's position — even when both are zero, so equality alone couldn't tell.
        var file = await ParseAsync(Statement("""
            <BUYMF><INVBUY>
              <INVTRAN><FITID>V-1</FITID><DTTRADE>20260105</DTTRADE><MEMO>MONEY FUND PURCHASE</MEMO></INVTRAN>
              <SECID><UNIQUEID>SETTL</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <UNITS>50</UNITS><UNITPRICE>1</UNITPRICE><TOTAL>-50.00</TOTAL>
            </INVBUY><BUYTYPE>BUY</BUYTYPE></BUYMF>
            <BUYMF><INVBUY>
              <INVTRAN><FITID>V-2</FITID><DTTRADE>20260106</DTTRADE><MEMO>BUY</MEMO></INVTRAN>
              <SECID><UNIQUEID>INDEX</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <UNITS>1</UNITS><UNITPRICE>50</UNITPRICE><TOTAL>-50.00</TOTAL>
            </INVBUY><BUYTYPE>BUY</BUYTYPE></BUYMF>
            """, positions: """
            <POSMF><INVPOS>
              <SECID><UNIQUEID>SETTL</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
              <HELDINACCT>CASH</HELDINACCT><POSTYPE>LONG</POSTYPE><UNITS>0</UNITS><UNITPRICE>1</UNITPRICE><MKTVAL>0</MKTVAL>
            </INVPOS></POSMF>
            """, balances: "<INVBAL><AVAILCASH>0</AVAILCASH><MARGINBALANCE>0</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>",
            brokerId: "vanguard.com"));

        var statement = file.Statements[0];
        statement.Transactions.Single(t => t.ExternalId == "V-1").IsSettlementFund.ShouldBeTrue();
        statement.Transactions.Single(t => t.ExternalId == "V-2").IsSettlementFund.ShouldBeFalse();
        statement.Positions.ShouldHaveSingleItem().IsSettlementFund.ShouldBeTrue();
        statement.Cash!.IncludesSettlementFund.ShouldBeTrue();
    }

    // ---------------- SGML (OFX 1.x) ----------------

    [Fact]
    public void SGML_leaf_tags_the_file_already_closes_are_not_closed_twice()
    {
        var xml = QfxFileParser.SgmlToXml("<OFX><A><FITID>1</FITID><MEMO>open leaf\n</A></OFX>");

        xml.ShouldBe("<OFX><A><FITID>1</FITID><MEMO>open leaf</MEMO>\n</A></OFX>");
    }

    [Fact]
    public void SGML_values_are_escaped_without_double_escaping_existing_entities()
    {
        var xml = QfxFileParser.SgmlToXml("<OFX><MEMO>AT&T &amp; Co &#38; more\n</OFX>");

        xml.ShouldBe("<OFX><MEMO>AT&amp;T &amp; Co &#38; more</MEMO>\n</OFX>");
    }

    [Fact]
    public async Task An_OFX1_file_with_closed_leaf_tags_parses_like_one_without()
    {
        const string closed = """
            OFXHEADER:100
            DATA:OFXSGML
            VERSION:102

            <OFX><INVSTMTMSGSRSV1><INVSTMTTRNRS><INVSTMTRS>
            <DTASOF>20260601</DTASOF>
            <INVACCTFROM><BROKERID>example.com</BROKERID><ACCTID>A-1</ACCTID></INVACCTFROM>
            <INVTRANLIST>
            <INCOME><INVTRAN><FITID>X-1</FITID><DTTRADE>20260115</DTTRADE><MEMO>A &amp; B</MEMO></INVTRAN>
            <SECID><UNIQUEID>XYZ</UNIQUEID><UNIQUEIDTYPE>TICKER</UNIQUEIDTYPE></SECID>
            <INCOMETYPE>DIV</INCOMETYPE><TOTAL>2.50</TOTAL></INCOME>
            </INVTRANLIST>
            </INVSTMTRS></INVSTMTTRNRS></INVSTMTMSGSRSV1></OFX>
            """;
        var open = closed.Replace("</FITID>", "").Replace("</DTTRADE>", "").Replace("</MEMO>", "")
            .Replace("</UNIQUEID>", "").Replace("</UNIQUEIDTYPE>", "").Replace("</INCOMETYPE>", "").Replace("</TOTAL>", "")
            .Replace("</DTASOF>", "").Replace("</BROKERID>", "").Replace("</ACCTID>", "");

        var fromClosed = (await Parser.ParseAsync(Stream(closed), "c.qfx", CancellationToken.None)).Statements[0].Transactions.Single();
        var fromOpen = (await Parser.ParseAsync(Stream(open), "o.qfx", CancellationToken.None)).Statements[0].Transactions.Single();

        fromClosed.ShouldBe(fromOpen);
        fromClosed.Memo.ShouldBe("A & B");
        fromClosed.Amount.ShouldBe(2.50m);
    }

    // ---------------- helpers ----------------

    private static Task<ParsedPortfolioFile> ParseAsync(string ofx)
        => Parser.ParseAsync(Stream(ofx), "test.qfx", CancellationToken.None);

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));

    /// <summary>An OFX 2 investment statement wrapping the given transaction, position and balance aggregates.</summary>
    private static string Statement(
        string transactions, string positions = "", string balances = "", string secList = "", string brokerId = "example.com") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <?OFX OFXHEADER="200" VERSION="202" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
        <OFX>
          <INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>1</TRNUID>
            <INVSTMTRS>
              <DTASOF>20260601120000</DTASOF>
              <CURDEF>USD</CURDEF>
              <INVACCTFROM><BROKERID>{brokerId}</BROKERID><ACCTID>ACCT-1</ACCTID></INVACCTFROM>
              <INVTRANLIST>
                <DTSTART>20260101</DTSTART><DTEND>20260601</DTEND>
                {transactions}
              </INVTRANLIST>
              {(positions.Length > 0 ? $"<INVPOSLIST>{positions}</INVPOSLIST>" : "")}
              {balances}
            </INVSTMTRS>
          </INVSTMTTRNRS></INVSTMTMSGSRSV1>
          {(secList.Length > 0 ? $"<SECLISTMSGSRSV1><SECLIST>{secList}</SECLIST></SECLISTMSGSRSV1>" : "")}
        </OFX>
        """;
}
