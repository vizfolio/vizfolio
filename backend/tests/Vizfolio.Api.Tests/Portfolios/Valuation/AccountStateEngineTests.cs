using Shouldly;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios.Valuation;

/// <summary>
/// The valuation engine on hand-built inputs (no database): how shares and cash roll, how openings are derived from
/// broker statements, how splits apply, how ledger/broker drift is judged, and how flows are valued.
/// </summary>
public sealed class AccountStateEngineTests
{
    private static readonly Guid Fund = Guid.NewGuid();
    private static readonly Guid Settlement = Guid.NewGuid();
    private static readonly DateOnly Jan2 = new(2025, 1, 2);
    private static readonly DateOnly Mar3 = new(2025, 3, 3);
    private static readonly DateOnly Jun2 = new(2025, 6, 2);
    private static readonly DateOnly Dec31 = new(2025, 12, 31);

    // ---------- shares ----------

    [Fact]
    public void Buys_reinvestments_and_transfers_add_shares_and_a_sell_removes_them_whatever_its_sign()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 2000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Mar3, TransactionType.Reinvest, -100m, Fund, 1m),
                Row(Mar3, TransactionType.Dividend, 100m),
                Row(Jun2, TransactionType.Transfer, 400m, Fund, 4m),
                Row(Jun2.AddDays(1), TransactionType.Sell, 500m, Fund, -5m),
            ],
            holdings: [Holding(Fund, "FUND", prices: [(Dec31, 100m)])]);

        var fund = engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund);

        fund.Quantity.ShouldBe(10m); // 10 + 1 + 4 − 5
        fund.Value.ShouldBe(1000m);
    }

    [Fact]
    public void Income_and_unrecognised_rows_move_shares_only_when_they_carry_a_quantity()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 1000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Mar3, TransactionType.Dividend, 20m, Fund, 0.2m), // reinvested at source (older reports)
                Row(Mar3, TransactionType.Other, 0m, Fund, -1m),       // e.g. an old "Sweep" moving shares out
                Row(Jun2, TransactionType.Other, 5m, Fund),             // no quantity: moves no shares
            ],
            holdings: [Holding(Fund, "FUND", prices: [(Dec31, 100m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(9.2m);
    }

    // ---------- cash ----------

    [Fact]
    public void A_cash_dividend_stays_in_the_account_as_cash()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 1000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Jun2, TransactionType.Dividend, 25m, Fund),
            ],
            holdings: [Holding(Fund, "FUND", prices: [(Dec31, 100m)])]);

        var value = engine.ValueAt(Dec31);

        value.Value.ShouldBe(1025m);
        value.Components.Single(c => c.IsCash).Value.ShouldBe(25m);
    }

    [Fact]
    public void Selling_to_cash_keeps_the_proceeds_in_the_account()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 1000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Jun2, TransactionType.Sell, 1200m, Fund, -10m),
            ],
            holdings: [Holding(Fund, "FUND", prices: [(Jun2, 120m), (Dec31, 130m)])]);

        var value = engine.ValueAt(Dec31);

        value.Value.ShouldBe(1200m);
        value.IsComplete.ShouldBeTrue();
        value.Components.Single(c => c.HoldingId == Fund).Status.ShouldBe(HoldingValuationStatus.NotHeld);
    }

    [Fact]
    public void A_sweep_reported_by_two_sources_moves_the_cash_only_once()
    {
        // The QFX reports the sweep into the settlement fund as a Buy of ZXMXX; the Vanguard report as a "Sweep in".
        // The settlement fund is cash, so neither changes the account's cash.
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 700m),
                Row(Jan2, TransactionType.Buy, -700m, Settlement, 700m, source: "QFX"),
                Row(Jan2, TransactionType.Other, -700m, Settlement, sourceType: "Sweep in"),
            ],
            holdings: [Holding(Settlement, "ZXMXX")]);

        engine.ValueAt(Dec31).Value.ShouldBe(700m);
    }

    [Fact]
    public void Money_moved_out_of_the_money_market_fund_and_back_in_as_cash_is_kept()
    {
        // A fund-company account converted to brokerage: the money-market balance is transferred out and the same
        // money arrives as cash. The account's cash is unchanged.
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 12_000.00m),
                Row(Jan2, TransactionType.Other, -12_000.00m, Settlement, sourceType: "Sweep in", ticker: "ZXCXX"),
                Row(Jun2, TransactionType.Transfer, -12_000.00m, Settlement, -12_000.00m, sourceType: "TRANSFER TO 1234", ticker: "ZXCXX"),
                Row(Jun2, TransactionType.Transfer, 12_000.40m, sourceType: "Transfer (incoming)"),
            ],
            holdings: [Holding(Settlement, "ZXCXX")]);

        engine.ValueAt(Dec31).Value.ShouldBe(12_000.40m);
    }

    [Fact]
    public void A_purchase_of_the_money_market_fund_with_outside_money_counts_once_via_its_implied_contribution()
    {
        // Its purchases carry share counts, so it's valued as an ordinary $1.00 holding; the implied contribution
        // is the money that bought it.
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Buy, 200m, Settlement, 200m, sourceType: "Buy", ticker: "ZXCXX"),
                Row(Jan2, TransactionType.Deposit, 200m, source: ImpliedContributionService.SourceSystem),
            ],
            holdings: [Holding(Settlement, "ZXCXX", stablePrice: 1m)]);

        engine.ValueAt(Dec31).Value.ShouldBe(200m);
    }

    // ---------- derived openings ----------

    [Fact]
    public void A_partial_history_starts_from_the_positions_and_cash_the_broker_statement_implies()
    {
        // An 18-month QFX: the account already held 50 shares and $300 cash before its first imported row. The
        // statement (60 shares, $100 in the settlement fund) rolled back over the imported trades recovers both.
        var statement = Dec31;
        var engine = Build(
            ledger:
            [
                Row(Mar3, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Jun2, TransactionType.Deposit, 800m),
            ],
            holdings:
            [
                Holding(Fund, "FUND", prices: [(Mar3.AddDays(-1), 95m), (Dec31, 110m)], anchors: [Anchor(statement, 60m, 6600m)]),
                Holding(Settlement, "ZXMXX", anchors: [Anchor(statement, 100m, 100m)], stablePrice: 1m),
            ]);

        var opening = engine.OpeningDate!.Value;
        opening.ShouldBe(Mar3.AddDays(-1));
        engine.Openings.Single(o => o.HoldingId == Fund).ShouldSatisfyAllConditions(
            o => o.Quantity.ShouldBe(50m),
            o => o.Class.ShouldBe(OpeningClass.PreHistory));
        engine.OpeningCashSeed.ShouldBe(300m); // 100 at the statement − 800 deposited + 1000 spent

        var start = engine.ValueAt(opening);
        start.Value.ShouldBe(50m * 95m + 300m);
        start.IsComplete.ShouldBeTrue();
        engine.Findings.ShouldNotContain(f => f.IsMaterial);
    }

    [Fact]
    public void A_complete_history_has_no_pre_history_position_and_no_opening_cash()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 1000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
            ],
            holdings: [Holding(Fund, "FUND", anchors: [Anchor(Dec31, 10m, 1100m)])]);

        engine.Openings.Single(o => o.HoldingId == Fund).Class.ShouldBe(OpeningClass.None);
        engine.OpeningCashSeed.ShouldBe(0m);
    }

    [Fact]
    public void Before_the_history_a_position_held_before_it_is_unknown_and_one_bought_within_it_is_not_held()
    {
        var other = Guid.NewGuid();
        var engine = Build(
            ledger: [Row(Mar3, TransactionType.Buy, -1000m, other, 10m), Row(Mar3, TransactionType.Deposit, 1000m)],
            holdings:
            [
                Holding(Fund, "OLD", prices: [(Jan2, 50m)], anchors: [Anchor(Dec31, 5m, 600m)]),
                Holding(other, "NEW", prices: [(Jan2, 100m)], anchors: [Anchor(Dec31, 10m, 1100m)]),
            ]);

        var value = engine.ValueAt(Jan2);

        value.Components.Single(c => c.HoldingId == Fund).Cause.ShouldBe(MissingCause.BeforeHistory);
        value.Components.Single(c => c.HoldingId == other).Status.ShouldBe(HoldingValuationStatus.NotHeld);
    }

    [Fact]
    public void A_position_held_but_untouched_in_a_window_is_valued_from_a_later_statement()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 1m)],
            holdings: [Holding(Fund, "QUIET", prices: [(Jun2, 55m)], anchors: [Anchor(new DateOnly(2026, 9, 30), 10m, 700m)])]);

        engine.ValueAt(Jun2).Components.Single(c => c.HoldingId == Fund).Value.ShouldBe(550m);
    }

    [Fact]
    public void A_statement_showing_fewer_shares_than_the_ledger_bought_makes_the_opening_inconsistent()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m), Row(Jan2, TransactionType.Deposit, 1000m)],
            holdings: [Holding(Fund, "FUND", anchors: [Anchor(Dec31, 1m, 110m)])]);

        engine.Openings.Single(o => o.HoldingId == Fund).Class.ShouldBe(OpeningClass.Inconsistent);
    }

    // ---------- splits ----------

    [Fact]
    public void A_provider_split_applies_on_its_ex_date_even_with_no_broker_row()
    {
        var exDate = Jun2;
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 4000m), Row(Jan2, TransactionType.Buy, -4000m, Fund, 10m)],
            holdings: [Holding(Fund, "SPLT", prices: [(exDate.AddDays(-1), 400m), (exDate, 100m)], splits: [new SplitAction(exDate, 4m)])]);

        engine.ValueAt(exDate.AddDays(-1)).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(10m);
        engine.ValueAt(exDate).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(40m);
    }

    [Fact]
    public void A_broker_split_row_a_few_days_after_the_ex_date_is_not_applied_twice()
    {
        var exDate = Jun2;
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 4000m),
                Row(Jan2, TransactionType.Buy, -4000m, Fund, 10m),
                Row(exDate.AddDays(3), TransactionType.Split, 0m, Fund),
            ],
            holdings: [Holding(Fund, "SPLT", prices: [(Dec31, 100m)], splits: [new SplitAction(exDate, 4m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(40m);
        engine.Findings.ShouldNotContain(f => f.Code == FindingCode.UnmatchedSplit);
    }

    [Fact]
    public void A_broker_row_that_only_records_the_splits_extra_shares_is_not_added_on_top()
    {
        // Some brokers record a 4:1 split as a no-cost receipt of 30 more shares near the ex-date.
        var exDate = Jun2;
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 4000m),
                Row(Jan2, TransactionType.Buy, -4000m, Fund, 10m),
                Row(exDate.AddDays(2), TransactionType.Transfer, 0m, Fund, 30m),
            ],
            holdings: [Holding(Fund, "SPLT", prices: [(Dec31, 100m)], splits: [new SplitAction(exDate, 4m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(40m);
    }

    [Fact]
    public void A_broker_split_row_with_no_provider_split_is_reported_and_changes_nothing()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m), Row(Jun2, TransactionType.Split, 0m, Fund)],
            holdings: [Holding(Fund, "SPLT", prices: [(Dec31, 100m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(10m);
        engine.Findings.ShouldContain(f => f.Code == FindingCode.UnmatchedSplit && f.Date == Jun2);
    }

    [Fact]
    public void A_broker_split_the_provider_doesnt_know_applies_its_own_ratio_from_its_date()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Jun2, TransactionType.Split, 0m, Fund, quantity: 20m) with { SplitFactor = 3m },
            ],
            holdings: [Holding(Fund, "SPLT", prices: [(Jun2.AddDays(-1), 300m), (Dec31, 100m)])]);

        engine.ValueAt(Jun2.AddDays(-1)).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(10m);
        // The ratio applies; the row's own change in shares (+20) is the same split and isn't added on top.
        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(30m);
        engine.Findings.ShouldContain(f => f.Code == FindingCode.UnmatchedSplit && f.Date == Jun2);
    }

    [Fact]
    public void A_broker_split_with_no_ratio_adds_its_change_in_shares()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m), Row(Jun2, TransactionType.Split, 0m, Fund, quantity: 10m)],
            holdings: [Holding(Fund, "SPLT", prices: [(Dec31, 50m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(20m);
    }

    [Fact]
    public void A_broker_split_ratio_defers_to_the_providers_split_for_the_same_event()
    {
        var exDate = new DateOnly(2025, 6, 2);
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(exDate.AddDays(2), TransactionType.Split, 0m, Fund, quantity: 30m) with { SplitFactor = 4m },
            ],
            holdings: [Holding(Fund, "SPLT", prices: [(Dec31, 25m)], splits: [new SplitAction(exDate, 4m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(40m);
        engine.Findings.ShouldNotContain(f => f.Code == FindingCode.UnmatchedSplit);
    }

    [Fact]
    public void Return_of_capital_is_cash_in_but_not_a_contribution_and_a_journal_moves_nothing()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 1000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
                Row(Mar3, TransactionType.ReturnOfCapital, 30m, Fund),
                Row(Jun2, TransactionType.Journal, 0m, Fund, quantity: 5m),
                Row(Jun2, TransactionType.Journal, 200m),
            ],
            holdings: [Holding(Fund, "FUND", prices: [(Dec31, 100m)])]);

        var value = engine.ValueAt(Dec31);
        value.Components.Single(c => c.HoldingId == Fund).Quantity.ShouldBe(10m);
        value.Components.Single(c => c.IsCash).Value.ShouldBe(30m);
        engine.FlowsBetween(Jan2, Dec31).ShouldHaveSingleItem().Kind.ShouldBe(FlowKind.Deposit);
    }

    [Fact]
    public void A_fund_the_broker_marks_as_its_settlement_fund_is_the_accounts_cash()
    {
        // Rows from a broker profile carry the flag; no "Sweep" label and no money market registry entry needed.
        var core = Guid.NewGuid();
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 500m),
                Row(Jan2, TransactionType.Buy, -500m, core, 500m, ticker: "CORE") with { IsSettlementFund = true },
            ],
            holdings: [Holding(core, "CORE")]);

        engine.SettlementTickers.ShouldContain("CORE");
        var value = engine.ValueAt(Dec31);
        value.IsComplete.ShouldBeTrue();
        value.Components.Single(c => c.IsCash).Value.ShouldBe(500m);
    }

    // ---------- reconciliation ----------

    [Fact]
    public void A_small_drift_from_the_broker_is_noted_but_still_valued()
    {
        // 1.5 shares short of the statement on a $100k account: worth ~$15 — immaterial.
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 100_000m), Row(Jan2, TransactionType.Buy, -100_000m, Fund, 10_000m)],
            holdings: [Holding(Fund, "FUND", prices: [(Jun2, 10m)], anchors: [Anchor(new DateOnly(2024, 12, 31), 0m, 0m), Anchor(Dec31, 10_001.5m, 100_015m)])]);

        engine.Findings.ShouldContain(f => f.Code == FindingCode.QuantityMismatch && !f.IsMaterial);
        engine.ValueAt(Jun2).IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void A_material_drift_from_the_broker_makes_the_position_unknown_until_that_statement()
    {
        // The ledger says 10 shares; the statement says 100. Between the two anchors the value can't be trusted.
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 1000m), Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m)],
            holdings: [Holding(Fund, "FUND", prices: [(Jun2, 100m), (Dec31, 100m)], anchors: [Anchor(new DateOnly(2024, 12, 31), 0m, 0m), Anchor(Dec31, 100m, 10_000m)])]);

        engine.Findings.ShouldContain(f => f.Code == FindingCode.QuantityMismatch && f.IsMaterial);
        engine.ValueAt(Jun2).Components.Single(c => c.HoldingId == Fund).Cause.ShouldBe(MissingCause.MaterialMismatch);
        engine.ValueAt(Dec31).IsComplete.ShouldBeTrue(); // from the statement on, the broker's position holds
    }

    // ---------- valuation fallbacks ----------

    [Fact]
    public void A_money_market_fund_is_valued_at_one_dollar_a_share_before_its_price_history_starts()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 50_000m), Row(Jan2, TransactionType.Buy, -50_000m, Fund, 50_000m)],
            holdings: [Holding(Fund, "ZXUXX", stablePrice: 1m)]);

        engine.ValueAt(Jun2).Components.Single(c => c.HoldingId == Fund).ShouldSatisfyAllConditions(
            c => c.Value.ShouldBe(50_000m),
            c => c.Source.ShouldBe(ComponentSource.StableNav));
    }

    [Fact]
    public void A_close_older_than_the_age_limit_leaves_the_position_missing_as_stale()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 1000m), Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m)],
            holdings: [Holding(Fund, "GONE", prices: [(Jan2, 100m)])]);

        engine.ValueAt(Dec31).Components.Single(c => c.HoldingId == Fund).Cause.ShouldBe(MissingCause.StalePrice);
    }

    [Fact]
    public void The_brokers_statement_is_used_as_is_until_shares_change_or_a_newer_price_arrives()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 1000m), Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m)],
            holdings: [Holding(Fund, "FUND", prices: [(Jun2.AddDays(-30), 120m)], anchors: [Anchor(Jun2, 10m, 1275m)])]);

        engine.ValueAt(Jun2.AddDays(5)).Components.Single(c => c.HoldingId == Fund).ShouldSatisfyAllConditions(
            c => c.Value.ShouldBe(1275m),
            c => c.Source.ShouldBe(ComponentSource.Snapshot));
    }

    // ---------- flows ----------

    [Fact]
    public void An_in_kind_transfer_with_no_amount_is_valued_at_the_days_price()
    {
        // A QFX <TRANSFER> (e.g. an ACAT into a new broker) reports shares but no amount.
        var engine = Build(
            ledger: [Row(Jun2, TransactionType.Transfer, 0m, Fund, 20m)],
            holdings: [Holding(Fund, "FUND", prices: [(Jun2, 50m)])]);

        var flow = engine.FlowsBetween(Jan2, Dec31).ShouldHaveSingleItem();
        flow.Amount.ShouldBe(1000m);
        flow.Kind.ShouldBe(FlowKind.InKindTransfer);
        flow.IsValued.ShouldBeTrue();
    }

    [Fact]
    public void An_in_kind_transfer_with_no_price_is_flagged_as_unvalued()
    {
        var engine = Build(
            ledger: [Row(Jun2, TransactionType.Transfer, 0m, Fund, -20m)],
            holdings: [Holding(Fund, "FUND")]);

        engine.FlowsBetween(Jan2, Dec31).ShouldHaveSingleItem().IsValued.ShouldBeFalse();
    }

    [Fact]
    public void Implied_contributions_are_flows_of_their_own_kind()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Deposit, 500m, source: ImpliedContributionService.SourceSystem)],
            holdings: []);

        engine.FlowsBetween(Jan2, Dec31).ShouldHaveSingleItem().Kind.ShouldBe(FlowKind.Implied);
    }

    // ---------- builders ----------

    // ---------- missing intervals (Data Health) ----------

    [Fact]
    public void MissingIntervals_merges_unvalued_days_into_runs_per_holding_and_cause()
    {
        var engine = Build(
            ledger:
            [
                Row(Jan2, TransactionType.Deposit, 1000m),
                Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m),
            ],
            holdings: [Holding(Fund, "FUND", prices: [(Mar3, 100m)])]);

        var gaps = engine.MissingIntervals(Jan2.AddDays(-1), new DateOnly(2025, 3, 31));

        gaps.Count.ShouldBe(2);
        gaps[0].ShouldBe(new MissingInterval(Fund, "FUND", MissingCause.NoPrice, Jan2, Mar3.AddDays(-1)));
        gaps[1].ShouldBe(new MissingInterval(Fund, "FUND", MissingCause.StalePrice, Mar3.AddDays(11), new DateOnly(2025, 3, 31)));
    }

    [Fact]
    public void MissingIntervals_is_empty_when_every_held_day_is_priced()
    {
        var engine = Build(
            ledger: [Row(Jan2, TransactionType.Buy, -1000m, Fund, 10m)],
            holdings: [Holding(Fund, "FUND", prices: [(Jan2, 100m), (Jan2.AddDays(8), 101m), (Jan2.AddDays(16), 102m)])]);

        engine.MissingIntervals(Jan2, Jan2.AddDays(20)).ShouldBeEmpty();
    }

    private static AccountStateEngine Build(IReadOnlyList<LedgerRow> ledger, IReadOnlyList<HoldingInput> holdings)
        => new(new AccountValuationInput(Guid.NewGuid(), ledger, holdings, new ValuationOptions()));

    private static LedgerRow Row(
        DateOnly date,
        TransactionType type,
        decimal amount,
        Guid? holdingId = null,
        decimal? quantity = null,
        string source = "TEST",
        string? sourceType = null,
        string? ticker = null)
    {
        ticker ??= holdingId == Settlement ? "ZXMXX" : holdingId is null ? null : "T" + holdingId.Value.ToString("N")[..5];
        return new LedgerRow(Guid.NewGuid(), source, date, null, type, holdingId, ticker, quantity, amount, null, sourceType);
    }

    private static HoldingInput Holding(
        Guid id,
        string symbol,
        (DateOnly Date, decimal Close)[]? prices = null,
        PositionAnchor[]? anchors = null,
        SplitAction[]? splits = null,
        decimal? stablePrice = null,
        bool isMoneyMarket = false)
    {
        var points = (prices ?? []).Select(p => new PricePointData(p.Date, p.Close)).ToList();
        return new HoldingInput(
            id,
            symbol,
            AccountHoldingKind.Fund,
            new PriceSeries(points, StablePrices.Resolve(stablePrice, points)),
            splits ?? [],
            (anchors ?? []).OrderBy(a => a.AsOf).ToList(),
            isMoneyMarket || stablePrice is not null);
    }

    private static PositionAnchor Anchor(DateOnly asOf, decimal quantity, decimal marketValue)
        => new(asOf, quantity, marketValue, AnchorSource.BrokerPosition);
}
