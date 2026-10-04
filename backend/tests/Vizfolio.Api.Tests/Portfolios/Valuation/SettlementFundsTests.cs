using Shouldly;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios.Valuation;

/// <summary>Which fund is the account's cash — decided from its own history, never from a ticker list.</summary>
public sealed class SettlementFundsTests
{
    private static readonly Guid Fund = Guid.NewGuid();
    private static readonly DateOnly Day = new(2025, 3, 3);

    [Fact]
    public void A_fund_on_a_sweep_row_is_the_settlement_fund_whatever_it_is()
    {
        var ledger = new[] { Row(TransactionType.Other, quantity: null, "Sweep in", ticker: "CORE") };

        SettlementFunds.Identify(ledger, []).ShouldBe(new[] { "CORE" });
    }

    [Fact]
    public void A_money_market_fund_whose_movements_have_no_share_counts_is_used_as_cash()
    {
        // Vanguard's report lists settlement-fund reinvestments without a quantity: its shares can't be rolled.
        var ledger = new[] { Row(TransactionType.Reinvest, quantity: null) };

        SettlementFunds.Identify(ledger, [Holding(isMoneyMarket: true)]).ShouldBe(new[] { "MMKT" });
    }

    [Fact]
    public void A_money_market_fund_only_on_statements_is_used_as_cash()
    {
        SettlementFunds.Identify([], [Holding(isMoneyMarket: true, onStatements: true)]).ShouldBe(new[] { "MMKT" });
    }

    [Fact]
    public void A_money_market_fund_with_complete_share_history_stays_an_ordinary_holding()
    {
        // e.g. a QFX recording sweeps as buys and sells of the fund, with quantities: its shares roll fine.
        var ledger = new[] { Row(TransactionType.Buy, quantity: 500m), Row(TransactionType.Sell, quantity: -200m) };

        SettlementFunds.Identify(ledger, [Holding(isMoneyMarket: true, onStatements: true)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_fund_that_is_not_a_money_market_fund_is_never_treated_as_cash()
    {
        var ledger = new[] { Row(TransactionType.Reinvest, quantity: null) };

        SettlementFunds.Identify(ledger, [Holding(isMoneyMarket: false, onStatements: true)]).ShouldBeEmpty();
    }

    private static LedgerRow Row(TransactionType type, decimal? quantity, string? sourceType = null, string ticker = "MMKT")
        => new(Guid.NewGuid(), "TEST", Day, null, type, Fund, ticker, quantity, 0m, null, sourceType);

    private static HoldingInput Holding(bool isMoneyMarket, bool onStatements = false)
        => new(
            Fund,
            "MMKT",
            AccountHoldingKind.Fund,
            PriceSeries.Empty,
            [],
            onStatements ? [new PositionAnchor(Day, 100m, 100m, AnchorSource.BrokerPosition)] : [],
            isMoneyMarket);
}
