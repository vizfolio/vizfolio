using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Application.Performance;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Infrastructure.Persistence;

namespace Vizfolio.Api.Tests.Performance;

public sealed class PerformanceCalculatorTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly PerformanceCalculator _calculator;

    public PerformanceCalculatorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _calculator = new PerformanceCalculator(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Unknown_portfolio_returns_null()
    {
        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Portfolio,
                Guid.NewGuid(),
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 12, 31),
                PerformanceGranularity.Monthly),
            CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_account_returns_null()
    {
        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account,
                Guid.NewGuid(),
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 12, 31),
                PerformanceGranularity.Monthly),
            CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Empty_account_returns_zero_summary_and_one_period()
    {
        var (_, accountId) = await SeedEmptyAccountAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account, accountId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Yearly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Summary.BeginningBalance.ShouldBe(0m);
        result.Summary.EndingBalance.ShouldBe(0m);
        result.Summary.Deposits.ShouldBe(0m);
        result.Summary.Withdrawals.ShouldBe(0m);
        result.Series.Count.ShouldBe(1);
        result.Summary.RateOfReturn.Simple.ShouldBeNull();
    }

    [Fact]
    public async Task Deposit_and_growth_compute_correct_summary()
    {
        var (_, accountId) = await SeedEmptyAccountAsync();
        // Beginning state on 2024-12-31: 1000 cash deposited 2024-12-01.
        AddTransaction(accountId, TransactionType.Deposit, new DateOnly(2024, 12, 1), 1000m);
        // During the period: another 500 deposit on 2025-06-01.
        AddTransaction(accountId, TransactionType.Deposit, new DateOnly(2025, 6, 1), 500m);
        // A buy on 2025-01-15 spending 1000 on shares (cash -1000, no snapshot yet -> need snapshot)
        AddTransaction(accountId, TransactionType.Buy, new DateOnly(2025, 1, 15), -1000m);
        // Snapshot for the holding at year end with market value 1200
        var holdingId = await SeedHoldingAsync(accountId, AccountHoldingKind.Security, "ACME");
        AddSnapshot(holdingId, new DateOnly(2025, 12, 31), quantity: 10m, marketValue: 1200m);
        // And a snapshot at year start (after the deposit but before buy) for clean beginning-balance accounting.
        AddSnapshot(holdingId, new DateOnly(2024, 12, 31), quantity: 0m, marketValue: 0m);
        await _db.SaveChangesAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account, accountId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Yearly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        // Beginning balance (end of 2024-12-31): 1000 cash + 0 holding value
        result.Summary.BeginningBalance.ShouldBe(1000m);
        // Ending balance (end of 2025-12-31):
        // Cash: 1000 (initial) - 1000 (buy) + 500 (mid-year deposit) = 500
        // Holding: market value 1200
        // Total: 1700
        result.Summary.EndingBalance.ShouldBe(1700m);
        result.Summary.Deposits.ShouldBe(500m);
        result.Summary.Withdrawals.ShouldBe(0m);
        result.Summary.NetCashFlow.ShouldBe(500m);
        // InvestmentReturn = 1700 - 1000 - 500 = 200
        result.Summary.InvestmentReturn.ShouldBe(200m);
        result.Summary.RateOfReturn.Simple.ShouldNotBeNull();
        result.Summary.RateOfReturn.ModifiedDietz.ShouldNotBeNull();
    }

    [Fact]
    public async Task Portfolio_scope_nets_intra_portfolio_transfers()
    {
        var portfolioId = await SeedPortfolioAsync();
        var acctA = await SeedAccountAsync(portfolioId, "a", "111");
        var acctB = await SeedAccountAsync(portfolioId, "b", "222");
        // External deposit on each side
        AddTransaction(acctA, TransactionType.Deposit, new DateOnly(2025, 1, 5), 1000m);
        AddTransaction(acctB, TransactionType.Deposit, new DateOnly(2025, 1, 5), 1000m);
        // Internal transfer 500 from A to B on same day — should net to zero.
        AddTransaction(acctA, TransactionType.Transfer, new DateOnly(2025, 3, 15), -500m);
        AddTransaction(acctB, TransactionType.Transfer, new DateOnly(2025, 3, 15), 500m);
        await _db.SaveChangesAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Portfolio, portfolioId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Yearly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Summary.Deposits.ShouldBe(2000m);
        result.Summary.Withdrawals.ShouldBe(0m);
        result.Summary.RateOfReturn.Notes
            .ShouldContain(n => n.Contains("intra-portfolio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Account_scope_keeps_transfers_as_cash_flows()
    {
        var portfolioId = await SeedPortfolioAsync();
        var acctA = await SeedAccountAsync(portfolioId, "a", "111");
        var acctB = await SeedAccountAsync(portfolioId, "b", "222");
        AddTransaction(acctA, TransactionType.Transfer, new DateOnly(2025, 3, 15), -500m);
        AddTransaction(acctB, TransactionType.Transfer, new DateOnly(2025, 3, 15), 500m);
        await _db.SaveChangesAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account, acctA,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Yearly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Summary.Withdrawals.ShouldBe(500m);
        result.Summary.Deposits.ShouldBe(0m);
    }

    [Fact]
    public async Task Monthly_granularity_produces_twelve_buckets_for_full_year()
    {
        var (_, accountId) = await SeedEmptyAccountAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account, accountId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Monthly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Series.Count.ShouldBe(12);
        result.Series[0].PeriodStart.ShouldBe(new DateOnly(2025, 1, 1));
        result.Series[0].PeriodEnd.ShouldBe(new DateOnly(2025, 1, 31));
        result.Series[11].PeriodEnd.ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public async Task Quarterly_granularity_produces_four_buckets_for_full_year()
    {
        var (_, accountId) = await SeedEmptyAccountAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account, accountId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Quarterly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Series.Count.ShouldBe(4);
        result.Series[0].PeriodEnd.ShouldBe(new DateOnly(2025, 3, 31));
        result.Series[3].PeriodEnd.ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public async Task Default_currency_falls_back_to_usd_when_no_holdings()
    {
        var (_, accountId) = await SeedEmptyAccountAsync();

        var result = await _calculator.CalculateAsync(
            new PerformanceRequest(
                PerformanceScope.Account, accountId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                PerformanceGranularity.Yearly),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Currency.ShouldBe("USD");
    }

    private async Task<Guid> SeedPortfolioAsync(string name = "P")
    {
        var portfolio = new Portfolio($"{name}-{Guid.NewGuid():N}");
        _db.Portfolios.Add(portfolio);
        await _db.SaveChangesAsync();
        return portfolio.PortfolioId;
    }

    private async Task<Guid> SeedAccountAsync(Guid portfolioId, string institutionCode, string accountNumber)
    {
        var account = new Account(portfolioId, $"{institutionCode}-{accountNumber}", institutionCode, accountNumber);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account.AccountId;
    }

    private async Task<(Guid PortfolioId, Guid AccountId)> SeedEmptyAccountAsync()
    {
        var portfolioId = await SeedPortfolioAsync();
        var accountId = await SeedAccountAsync(portfolioId, "fidelity.com", "12345");
        return (portfolioId, accountId);
    }

    private async Task<Guid> SeedHoldingAsync(Guid accountId, AccountHoldingKind kind, string symbol)
    {
        var holding = new AccountHolding(accountId, kind);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip: null);
        _db.AccountHoldings.Add(holding);
        await _db.SaveChangesAsync();
        return holding.AccountHoldingId;
    }

    private void AddTransaction(Guid accountId, TransactionType type, DateOnly date, decimal amount)
    {
        var tx = new AccountTransaction(
            accountId, "TEST", $"{type}-{date:yyyyMMdd}-{Guid.NewGuid():N}",
            type, date, amount);
        _db.AccountTransactions.Add(tx);
    }

    private void AddSnapshot(Guid holdingId, DateOnly asOf, decimal quantity, decimal marketValue)
    {
        var snap = new AccountHoldingSnapshot(holdingId, asOf, quantity, AccountHoldingSnapshotSource.Statement);
        snap.SetValuation(costBasis: null, marketValue: marketValue, unitPrice: null, currencyCode: null);
        _db.AccountHoldingSnapshots.Add(snap);
    }
}
