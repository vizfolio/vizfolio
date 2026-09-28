using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Api.Tests.Extracts;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class PortfolioPerformanceServiceTests
{
    private static readonly DateOnly From = new(2025, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 1);

    [Fact]
    public async Task Account_scope_ending_snapshot_for_every_holding_reports_complete_sum()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var h1 = await SeedHoldingAsync(ctx, accountId);
        var h2 = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, h1, To, marketValue: 1000m);
        await SeedSnapshotAsync(ctx, h2, To, marketValue: 500m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(1500m);
        result.EndingBalance.IsComplete.ShouldBeTrue();
        result.EndingBalance.SnapshotAsOf.ShouldBe(To);
        result.EndingBalance.HoldingsCovered.ShouldBe(2);
        result.EndingBalance.HoldingsMissingSnapshot.ShouldBe(0);
    }

    [Fact]
    public async Task Holding_bought_after_from_is_zero_and_complete_at_from()
    {
        // The holding is acquired mid-window (after From), so it was genuinely not held at From: its
        // true starting value is $0 and complete — it must not be counted as a missing snapshot (§7).
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 1), TransactionType.Buy, amount: -500m, quantity: 5m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 900m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.Value.ShouldBe(0m);
        result.StartingBalance.IsComplete.ShouldBeTrue();
        result.StartingBalance.SnapshotAsOf.ShouldBeNull();
        result.StartingBalance.HoldingsCovered.ShouldBe(0);
        result.StartingBalance.HoldingsMissingSnapshot.ShouldBe(0);
    }

    [Fact]
    public async Task Portfolio_scope_sums_balances_across_all_accounts()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolioId = await SeedPortfolioAsync(ctx);
        var acct1 = await SeedAccountAsync(ctx, portfolioId, "AAA-111");
        var acct2 = await SeedAccountAsync(ctx, portfolioId, "BBB-222");
        var h1 = await SeedHoldingAsync(ctx, acct1);
        var h2 = await SeedHoldingAsync(ctx, acct2);
        await SeedSnapshotAsync(ctx, h1, To, marketValue: 700m);
        await SeedSnapshotAsync(ctx, h2, To, marketValue: 300m);

        var service = NewService(ctx);
        var result = await service.ComputeForPortfolioAsync(portfolioId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(1000m);
        result.EndingBalance.HoldingsCovered.ShouldBe(2);
    }

    [Fact]
    public async Task Multiple_snapshots_before_to_selects_the_latest_one_at_or_before_to()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 6, 1), marketValue: 500m);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 12, 31), marketValue: 900m); // <= To
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2026, 2, 1), marketValue: 1200m); // > To, ignored

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(900m);
        result.EndingBalance.SnapshotAsOf.ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public async Task Snapshot_with_null_market_value_is_counted_as_missing()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: null);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(0m);
        result.EndingBalance.HoldingsCovered.ShouldBe(0);
        result.EndingBalance.HoldingsMissingSnapshot.ShouldBe(1);
        result.EndingBalance.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task Holding_with_no_activity_and_no_snapshot_is_ignored_from_missing_count()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var active = await SeedHoldingAsync(ctx, accountId);
        var dormant = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, active, To, marketValue: 1000m);
        // dormant holding has no snapshot and no transactions in range — should be excluded entirely.

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        _ = dormant;
        result.EndingBalance.Value.ShouldBe(1000m);
        result.EndingBalance.HoldingsCovered.ShouldBe(1);
        result.EndingBalance.HoldingsMissingSnapshot.ShouldBe(0);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task Holding_held_at_from_without_valuation_is_missing_for_starting_balance()
    {
        // The holding is bought *before* From (held at From) but has no valued snapshot or price at/before
        // From, so its starting value is genuinely unknown → missing/incomplete (the honest-null case).
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2024, 6, 1), TransactionType.Buy, amount: -100m, quantity: 10m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 150m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.HoldingsMissingSnapshot.ShouldBe(1);
        result.StartingBalance.IsComplete.ShouldBeFalse();
        result.EndingBalance.HoldingsCovered.ShouldBe(1);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task From_after_to_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 100m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(
            portfolioId, accountId, new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_portfolio_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var service = NewService(ctx);

        var result = await service.ComputeForPortfolioAsync(Guid.NewGuid(), From, To, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Account_in_a_different_portfolio_returns_null()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var otherPortfolioId = await SeedPortfolioAsync(ctx, name: "Other");

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(otherPortfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Currency_defaults_to_USD_when_no_snapshots()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, _) = await SeedPortfolioWithAccountAsync(ctx);

        var service = NewService(ctx);
        var result = await service.ComputeForPortfolioAsync(portfolioId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public async Task Currency_selects_the_mode_when_snapshots_disagree()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var h1 = await SeedHoldingAsync(ctx, accountId);
        var h2 = await SeedHoldingAsync(ctx, accountId);
        var h3 = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, h1, To, marketValue: 100m, currency: "USD");
        await SeedSnapshotAsync(ctx, h2, To, marketValue: 100m, currency: "USD");
        await SeedSnapshotAsync(ctx, h3, To, marketValue: 100m, currency: "EUR");

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.CurrencyCode.ShouldBe("USD");
    }

    [Fact]
    public async Task Service_does_not_persist_any_changes()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 42m);

        var beforeSnapshots = await ctx.Db.AccountHoldingSnapshots.CountAsync();
        var beforeHoldings = await ctx.Db.AccountHoldings.CountAsync();

        var service = NewService(ctx);
        _ = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);
        _ = await service.ComputeForPortfolioAsync(portfolioId, From, To, CancellationToken.None);

        (await ctx.Db.AccountHoldingSnapshots.CountAsync()).ShouldBe(beforeSnapshots);
        (await ctx.Db.AccountHoldings.CountAsync()).ShouldBe(beforeHoldings);
        ctx.Db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Default_from_falls_back_to_earliest_transaction_date_when_not_supplied()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        var earliestTrade = new DateOnly(2025, 3, 15);
        await SeedTransactionAsync(ctx, accountId, holding, earliestTrade, TransactionType.Buy, amount: -100m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 9, 1), TransactionType.Buy, amount: -100m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 300m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.From.ShouldBe(earliestTrade);
    }

    [Fact]
    public async Task Contributions_include_deposits_withdrawals_transfers_in_range()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);

        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 3, 1), TransactionType.Deposit, amount: 1000m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 7, 1), TransactionType.Withdrawal, amount: -200m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 9, 1), TransactionType.Transfer, amount: 50m);
        // Not contributions:
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 4, 1), TransactionType.Buy, amount: -500m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 5, 1), TransactionType.Dividend, amount: 25m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 1), TransactionType.Reinvest, amount: 25m);

        await SeedSnapshotAsync(ctx, holding, To, marketValue: 900m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Contributions.Net.ShouldBe(850m);       // 1000 - 200 + 50
        result.Contributions.Deposits.ShouldBe(1050m); // 1000 + 50 (positive Transfer)
        result.Contributions.Withdrawals.ShouldBe(-200m);
        result.Contributions.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Contributions_are_zero_when_no_flows_in_range()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1000m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Contributions.Net.ShouldBe(0m);
        result.Contributions.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Returns_are_null_with_reason_when_starting_balance_is_incomplete()
    {
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        // Held at From (bought before the window) but unvalued there → incomplete starting balance.
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2024, 6, 1), TransactionType.Buy, amount: -500m, quantity: 10m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 900m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Returns.TimeWeighted.Rate.ShouldBeNull();
        result.Returns.TimeWeighted.Reason.ShouldBe("IncompleteStartingBalance");
        result.Returns.MoneyWeighted.Rate.ShouldBeNull();
        result.Returns.MoneyWeighted.Reason.ShouldBe("IncompleteStartingBalance");
    }

    [Fact]
    public async Task Returns_are_computed_when_starting_and_ending_snapshots_both_exist()
    {
        // Opening snapshot at the close of the day before From ($1000) — the period's start — and an
        // ending snapshot at To ($1100), no cash flows.
        // Modified Dietz: R = 100 / 1000 = 10% period.
        // XIRR: 10% annualized (365-day period).
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, From.AddDays(-1), marketValue: 1000m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1100m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.IsComplete.ShouldBeTrue();
        result.EndingBalance.IsComplete.ShouldBeTrue();

        result.Returns.TimeWeighted.Rate.ShouldNotBeNull();
        Math.Abs(result.Returns.TimeWeighted.Rate!.Value - 0.10m).ShouldBeLessThan(0.0001m);
        result.Returns.TimeWeighted.Method.ShouldBe("ModifiedDietz");
        result.Returns.TimeWeighted.Basis.ShouldBe("Period");

        result.Returns.MoneyWeighted.Rate.ShouldNotBeNull();
        Math.Abs(result.Returns.MoneyWeighted.Rate!.Value - 0.10m).ShouldBeLessThan(0.001m);
        result.Returns.MoneyWeighted.Method.ShouldBe("XIRR");
        result.Returns.MoneyWeighted.Basis.ShouldBe("Annualized");
    }

    [Fact]
    public async Task Chained_TWRR_returns_null_reason_when_only_boundary_snapshots_exist()
    {
        // Same setup as above, but swap in the Chained calculator directly to verify
        // it reports InsufficientIntermediateSnapshots for the boundary-only case.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, From, marketValue: 1000m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1100m);

        var service = new PortfolioPerformanceService(
            ctx.Db,
            new ChainedSubPeriodTimeWeightedReturnCalculator(),
            new XirrMoneyWeightedReturnCalculator());
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Returns.TimeWeighted.Method.ShouldBe("ChainedSubPeriods");
        result.Returns.TimeWeighted.Rate.ShouldBeNull();
        result.Returns.TimeWeighted.Reason.ShouldBe("InsufficientIntermediateSnapshots");
    }

    [Fact]
    public async Task Chained_TWRR_uses_interior_snapshots_when_they_exist()
    {
        // Opening boundary at the start of From ($1000), interior at day 180 ($1100), boundary at To ($1300).
        // sub1 = 1.10, sub2 = 1300/1100. Total = 1.30 - 1 = 0.30.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingAsync(ctx, accountId);
        await SeedSnapshotAsync(ctx, holding, From.AddDays(-1), marketValue: 1000m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedSnapshotAsync(ctx, holding, From.AddDays(180), marketValue: 1100m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1300m);

        var service = new PortfolioPerformanceService(
            ctx.Db,
            new ChainedSubPeriodTimeWeightedReturnCalculator(),
            new XirrMoneyWeightedReturnCalculator());
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Returns.TimeWeighted.Rate.ShouldNotBeNull();
        Math.Abs(result.Returns.TimeWeighted.Rate!.Value - 0.30m).ShouldBeLessThan(0.0001m);
    }

    // ---------- PriceHistory valuation scenarios (docs/price-history-valuation.md §8 checklist) ----------

    [Fact]
    public async Task Mid_history_window_values_from_price_history_and_returns_a_sensible_rate()
    {
        // §2 repro: full ledger with a $0 opening balance at inception and one far-end snapshot. Without
        // PriceHistory a mid-history `from` reuses the stale $0 and the TWR explodes. With a price at
        // `from`, the starting balance is the honest quantity × price and the return is sensible.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "VOO");

        var inception = new DateOnly(2024, 1, 2);
        await SeedSnapshotAsync(ctx, holding, inception.AddDays(-1), marketValue: 0m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedTransactionAsync(ctx, accountId, holding, inception, TransactionType.Buy, amount: -8000m, quantity: 10m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 11000m);

        // Raw prices: 900 at the start of the mid-history `from`, 1100 at `to` → BMV 9000, EMV 11000, TWR ≈ 22%.
        await SeedPriceAsync(ctx, "VOO", From.AddDays(-1), close: 900m);
        await SeedPriceAsync(ctx, "VOO", To, close: 1100m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.Value.ShouldBe(9000m);
        result.StartingBalance.IsComplete.ShouldBeTrue();
        result.EndingBalance.Value.ShouldBe(11000m);
        result.Returns.TimeWeighted.Rate.ShouldNotBeNull();
        result.Returns.TimeWeighted.Rate!.Value.ShouldBeInRange(0.20m, 0.25m); // not tens of thousands
    }

    [Fact]
    public async Task Held_holding_with_no_price_and_no_boundary_snapshot_stays_incomplete()
    {
        // Held at `from` (bought before the window) but neither priced nor snapshotted there → honest null.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "NOPX");
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2024, 6, 1), TransactionType.Buy, amount: -1000m, quantity: 10m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1500m);
        await SeedPriceAsync(ctx, "NOPX", To, close: 150m); // price only at the far end, none near `from`

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.IsComplete.ShouldBeFalse();
        result.StartingBalance.HoldingsMissingSnapshot.ShouldBe(1);
        result.Returns.TimeWeighted.Rate.ShouldBeNull();
        result.Returns.TimeWeighted.Reason.ShouldBe("IncompleteStartingBalance");
    }

    [Fact]
    public async Task Staggered_account_inceptions_report_starting_balance_complete()
    {
        // §7: accounts started 2011, 2011, 2014. At the portfolio-wide `from` (2011) the 2014 account's
        // holding was not held → $0 and complete, so the starting balance is complete overall.
        await using var ctx = await TestDbContext.CreateAsync();
        var portfolioId = await SeedPortfolioAsync(ctx);
        var acct2011a = await SeedAccountAsync(ctx, portfolioId, "A-2011");
        var acct2011b = await SeedAccountAsync(ctx, portfolioId, "B-2011");
        var acct2014 = await SeedAccountAsync(ctx, portfolioId, "C-2014");

        await SeedOpeningAndBuy(ctx, acct2011a, new DateOnly(2011, 1, 3), quantity: 10m);
        await SeedOpeningAndBuy(ctx, acct2011b, new DateOnly(2011, 1, 3), quantity: 5m);
        await SeedOpeningAndBuy(ctx, acct2014, new DateOnly(2014, 1, 3), quantity: 8m);

        var service = NewService(ctx);
        var result = await service.ComputeForPortfolioAsync(portfolioId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.From.ShouldBe(new DateOnly(2011, 1, 3)); // earliest transaction across accounts
        result.StartingBalance.IsComplete.ShouldBeTrue();
        result.StartingBalance.HoldingsMissingSnapshot.ShouldBe(0);
    }

    [Fact]
    public async Task Since_inception_boundary_snapshots_are_unchanged_by_price_wiring()
    {
        // Regression guard: with no PriceHistory, a boundary-snapshot-only holding values exactly as before
        // (opening $1000 → ending $1100 = 10% period), and prices for an unrelated series don't leak in.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "AAA");
        await SeedSnapshotAsync(ctx, holding, From.AddDays(-1), marketValue: 1000m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1100m);
        await SeedPriceAsync(ctx, "ZZZ", From.AddDays(-1), close: 999m); // unrelated series — must be ignored

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, From, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.Value.ShouldBe(1000m);
        result.EndingBalance.Value.ShouldBe(1100m);
        result.Returns.TimeWeighted.Rate.ShouldNotBeNull();
        Math.Abs(result.Returns.TimeWeighted.Rate!.Value - 0.10m).ShouldBeLessThan(0.0001m);
    }

    [Fact]
    public async Task Split_with_matching_corporate_action_values_the_adjusted_quantity()
    {
        // 4-for-1 split: 10 shares → 40. Raw price 30 on the valuation date → market value 40 × 30 = 1200.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "SPLIT");

        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2024, 6, 1), TransactionType.Buy, amount: -4000m, quantity: 10m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 1), TransactionType.Split, amount: 0m);
        await SeedSplitAsync(ctx, "SPLIT", new DateOnly(2025, 6, 1), numerator: 4m, denominator: 1m);

        var valuationDate = new DateOnly(2025, 7, 1);
        await SeedPriceAsync(ctx, "SPLIT", valuationDate, close: 30m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(
            portfolioId, accountId, new DateOnly(2024, 1, 1), valuationDate, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(1200m);
        result.EndingBalance.HoldingsCovered.ShouldBe(1);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task Ledger_missing_early_history_does_not_override_a_zero_broker_snapshot()
    {
        // Regression: a closed fund whose ledger lacks its original purchase. Rolling the ledger from zero
        // gives −10 shares (more transferred out than recorded in) and priced that to a negative balance.
        // The broker snapshot says 0 shares / $0, which must win.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "VBMFX");

        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2016, 1, 4), TransactionType.Buy, amount: -1000m, quantity: 100m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2018, 6, 1), TransactionType.Transfer, amount: -1100m, quantity: -110m);
        await SeedSnapshotAsync(ctx, holding, new DateOnly(2018, 6, 30), marketValue: 0m);
        await SeedPriceAsync(ctx, "VBMFX", To, close: 10m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(0m);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task Quantity_rolls_forward_from_the_latest_broker_snapshot_not_from_zero()
    {
        // The ledger only covers trades after the snapshot (e.g. an 18-month QFX), so the broker's 100
        // shares anchor the position and the later 10-share buy is added on top: 110 × $50.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "ANCH");

        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 1, 31), marketValue: 4000m, quantity: 100m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 3, 3), TransactionType.Buy, amount: -450m, quantity: 10m);
        await SeedPriceAsync(ctx, "ANCH", To, close: 50m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, new DateOnly(2025, 1, 31), To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(5500m);
    }

    [Fact]
    public async Task Broker_snapshot_newer_than_the_latest_price_is_used_as_is()
    {
        // Prices stop a month before the broker's statement and nothing traded since: the statement's
        // market value is the freshest truth and matches what the Holdings view shows.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "STALE");

        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2024, 6, 1), TransactionType.Buy, amount: -1000m, quantity: 10m);
        await SeedPriceAsync(ctx, "STALE", To.AddDays(-30), close: 120m);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1275m, quantity: 10m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, new DateOnly(2024, 6, 1), To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(1275m);
        result.EndingBalance.SnapshotAsOf.ShouldBe(To);
    }

    [Fact]
    public async Task Unpriced_position_sold_to_zero_after_its_snapshot_is_worth_zero_not_the_stale_snapshot()
    {
        // Traditional IRA → Roth: an opening balance of 100 shares / $1,000 on the buy date, reinvested
        // dividends, then everything converted out. No price history is loaded, so valuation falls back to
        // the snapshot — which must not resurrect the old $1,000 once the shares are gone.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "VBMFX");

        var opening = new DateOnly(2016, 2, 1);
        await SeedTransactionAsync(ctx, accountId, holding, opening, TransactionType.Buy, amount: -1000m, quantity: 100m);
        await SeedSnapshotAsync(ctx, holding, opening, marketValue: 1000m, source: AccountHoldingSnapshotSource.OpeningBalance, quantity: 100m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2016, 3, 31), TransactionType.Dividend, amount: 20m, quantity: 2m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2018, 11, 20), TransactionType.Transfer, amount: -1050m, quantity: -102m);

        // The period opens the day after the snapshot, so its starting balance is the snapshot's $1,000.
        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, opening.AddDays(1), To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.StartingBalance.Value.ShouldBe(1000m);
        result.EndingBalance.Value.ShouldBe(0m);
        result.EndingBalance.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task Unpriced_position_that_changed_since_its_snapshot_is_revalued_at_the_snapshots_unit_price()
    {
        // 100 shares worth $4,000 ($40/share) at the snapshot, then 25 sold; with no price series the
        // remaining 75 shares are carried at the last known $40.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "NOPX");

        await SeedSnapshotAsync(ctx, holding, new DateOnly(2025, 1, 31), marketValue: 4000m, quantity: 100m);
        await SeedTransactionAsync(ctx, accountId, holding, new DateOnly(2025, 6, 2), TransactionType.Sell, amount: 1100m, quantity: -25m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, new DateOnly(2025, 1, 31), To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.EndingBalance.Value.ShouldBe(3000m);
    }

    [Fact]
    public async Task A_purchase_on_the_first_day_is_a_flow_not_part_of_the_starting_balance()
    {
        // Regression: the period used to open at the *close* of `from`, so a purchase that day was counted
        // twice — in the starting balance and again as its (e.g. implied) contribution. It opens at the
        // close of the previous day: $0 here, with the $1,000 funding the purchase as the only flow.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "FIRST");
        var firstDay = new DateOnly(2025, 3, 3);

        await SeedSnapshotAsync(ctx, holding, firstDay.AddDays(-1), marketValue: 0m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedCashAsync(ctx, accountId, firstDay, TransactionType.Deposit, 1000m);
        await SeedTransactionAsync(ctx, accountId, holding, firstDay, TransactionType.Buy, amount: -1000m, quantity: 100m);
        await SeedPriceAsync(ctx, "FIRST", firstDay, close: 10m);
        await SeedPriceAsync(ctx, "FIRST", To, close: 11m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        result.From.ShouldBe(firstDay);
        result.StartingBalance.Value.ShouldBe(0m);
        result.Contributions.Net.ShouldBe(1000m);
        result.EndingBalance.Value.ShouldBe(1100m);
        result.Returns.TimeWeighted.Rate!.Value.ShouldBe(0.10m, tolerance: 0.0001m); // not ~ -45% or +110%
    }

    [Fact]
    public async Task Value_series_agrees_with_the_balances_and_carries_the_contributions()
    {
        // Priced holding bought with a deposit: the chart's first point is the starting balance, its last
        // is the ending balance, and its flows add up to the period's net contributions.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "SERIES");
        var firstDay = new DateOnly(2025, 3, 3);

        await SeedSnapshotAsync(ctx, holding, firstDay.AddDays(-1), marketValue: 0m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedCashAsync(ctx, accountId, firstDay, TransactionType.Deposit, 1000m);
        await SeedTransactionAsync(ctx, accountId, holding, firstDay, TransactionType.Buy, amount: -1000m, quantity: 100m);
        await SeedPriceAsync(ctx, "SERIES", firstDay, close: 10m);
        await SeedPriceAsync(ctx, "SERIES", new DateOnly(2025, 6, 30), close: 12m);
        await SeedPriceAsync(ctx, "SERIES", To, close: 11m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        var points = result.Series.Points;
        result.Series.Interval.ShouldBe(PerformanceSeriesInterval.Monthly);
        points[0].Value.ShouldBe(result.StartingBalance.Value);
        points[^1].Date.ShouldBe(To);
        points[^1].Value.ShouldBe(result.EndingBalance.Value);
        points.Single(p => p.Date == new DateOnly(2025, 6, 30)).Value.ShouldBe(1200m); // 100 × $12
        points.Sum(p => p.Deposits + p.Withdrawals).ShouldBe(result.Contributions.Net);
    }

    [Fact]
    public async Task Returns_series_ends_at_the_headline_return_and_the_period_investment_gain()
    {
        // The returns chart must agree with the headline cards: its last cumulative return is the
        // time-weighted return, and its last gain is ending − starting − net contributions.
        await using var ctx = await TestDbContext.CreateAsync();
        var (portfolioId, accountId) = await SeedPortfolioWithAccountAsync(ctx);
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, "RETSERIES");
        var firstDay = new DateOnly(2025, 3, 3);

        await SeedSnapshotAsync(ctx, holding, firstDay.AddDays(-1), marketValue: 0m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedCashAsync(ctx, accountId, firstDay, TransactionType.Deposit, 1000m);
        await SeedTransactionAsync(ctx, accountId, holding, firstDay, TransactionType.Buy, amount: -1000m, quantity: 100m);
        await SeedPriceAsync(ctx, "RETSERIES", firstDay, close: 10m);
        await SeedPriceAsync(ctx, "RETSERIES", new DateOnly(2025, 6, 30), close: 12m);
        await SeedPriceAsync(ctx, "RETSERIES", To, close: 11m);

        var service = NewService(ctx);
        var result = await service.ComputeForAccountAsync(portfolioId, accountId, from: null, To, CancellationToken.None);

        result.ShouldNotBeNull();
        var points = result.Series.Points;
        result.Returns.TimeWeighted.Rate.ShouldNotBeNull();
        points[0].CumulativeReturn.ShouldBe(0m);
        points[^1].CumulativeReturn.ShouldBe(result.Returns.TimeWeighted.Rate);
        points[^1].InvestmentGain.ShouldBe(
            result.EndingBalance.Value - result.StartingBalance.Value - result.Contributions.Net);
        points.Single(p => p.Date == new DateOnly(2025, 6, 30)).InvestmentGain.ShouldBe(200m); // 1200 − 0 − 1000
    }

    private static PortfolioPerformanceService NewService(TestDbContext ctx) =>
        new(ctx.Db,
            new ModifiedDietzTimeWeightedReturnCalculator(),
            new XirrMoneyWeightedReturnCalculator());

    // ---------- seeding helpers ----------

    private static async Task<Guid> SeedPortfolioAsync(TestDbContext ctx, string name = "Test Portfolio")
    {
        var portfolio = new Portfolio(name);
        ctx.Db.Portfolios.Add(portfolio);
        await ctx.Db.SaveChangesAsync();
        return portfolio.PortfolioId;
    }

    private static async Task<Guid> SeedAccountAsync(TestDbContext ctx, Guid portfolioId, string accountNumber)
    {
        var account = new Account(portfolioId, $"acct {accountNumber}", "vanguard.com", accountNumber);
        ctx.Db.Accounts.Add(account);
        await ctx.Db.SaveChangesAsync();
        return account.AccountId;
    }

    private static async Task<(Guid PortfolioId, Guid AccountId)> SeedPortfolioWithAccountAsync(TestDbContext ctx)
    {
        var portfolioId = await SeedPortfolioAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, portfolioId, "1111");
        return (portfolioId, accountId);
    }

    private static async Task<Guid> SeedHoldingAsync(TestDbContext ctx, Guid accountId)
    {
        var holding = new AccountHolding(accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers($"SYM{Guid.NewGuid():N}"[..8], name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    private static async Task<Guid> SeedHoldingWithSymbolAsync(TestDbContext ctx, Guid accountId, string symbol)
    {
        var holding = new AccountHolding(accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip: null);
        ctx.Db.AccountHoldings.Add(holding);
        await ctx.Db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    private static async Task SeedPriceAsync(
        TestDbContext ctx, string symbol, DateOnly asOf, decimal close, string? currency = "USD")
    {
        ctx.Db.PriceHistories.Add(PriceHistory.ForSymbol(symbol, asOf, close, currency, PriceSource.Stooq));
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedSplitAsync(
        TestDbContext ctx, string symbol, DateOnly exDate, decimal numerator, decimal denominator)
    {
        ctx.Db.CorporateActions.Add(
            CorporateAction.SplitForSymbol(symbol, exDate, numerator, denominator, PriceSource.Eodhd));
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedOpeningAndBuy(
        TestDbContext ctx, Guid accountId, DateOnly inception, decimal quantity)
    {
        var holding = await SeedHoldingWithSymbolAsync(ctx, accountId, $"S{Guid.NewGuid():N}"[..6]);
        await SeedSnapshotAsync(ctx, holding, inception.AddDays(-1), marketValue: 0m, source: AccountHoldingSnapshotSource.OpeningBalance);
        await SeedTransactionAsync(ctx, accountId, holding, inception, TransactionType.Buy, amount: -1000m, quantity: quantity);
        await SeedSnapshotAsync(ctx, holding, To, marketValue: 1000m);
    }

    private static async Task SeedSnapshotAsync(
        TestDbContext ctx,
        Guid holdingId,
        DateOnly asOf,
        decimal? marketValue,
        string? currency = "USD",
        AccountHoldingSnapshotSource source = AccountHoldingSnapshotSource.BrokerPosition,
        decimal? quantity = null)
    {
        // A $0 snapshot means nothing is held; otherwise default to a nominal 1-unit position.
        var units = quantity ?? (marketValue == 0m ? 0m : 1m);
        var snapshot = new AccountHoldingSnapshot(holdingId, asOf, units, source);
        snapshot.SetValuation(costBasis: null, marketValue, unitPrice: null, currency);
        ctx.Db.AccountHoldingSnapshots.Add(snapshot);
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedCashAsync(
        TestDbContext ctx, Guid accountId, DateOnly tradeDate, TransactionType type, decimal amount)
    {
        ctx.Db.AccountTransactions.Add(new AccountTransaction(
            accountId, sourceSystem: "TEST", externalId: Guid.NewGuid().ToString("N"), type, tradeDate, amount));
        await ctx.Db.SaveChangesAsync();
    }

    private static async Task SeedTransactionAsync(
        TestDbContext ctx,
        Guid accountId,
        Guid holdingId,
        DateOnly tradeDate,
        TransactionType type,
        decimal amount,
        decimal? quantity = null)
    {
        var tx = new AccountTransaction(
            accountId,
            sourceSystem: "TEST",
            externalId: Guid.NewGuid().ToString("N"),
            type,
            tradeDate,
            amount);
        tx.LinkToHolding(holdingId);
        if (quantity.HasValue)
            tx.SetTradeDetails(quantity, price: null, fees: null, settlementDate: null);
        ctx.Db.AccountTransactions.Add(tx);
        await ctx.Db.SaveChangesAsync();
    }
}
