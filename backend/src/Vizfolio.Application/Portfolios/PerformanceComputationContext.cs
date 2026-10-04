namespace Vizfolio.Application.Portfolios;

/// <summary>
/// Everything a return strategy needs for one period. <see cref="ValueAt"/> values the scope at the close of any
/// date (the same valuation as the balances); strategies that only need the boundary balances ignore it.
/// </summary>
public sealed record PerformanceComputationContext(
    DateOnly From,
    DateOnly To,
    decimal StartingBalance,
    bool StartingIsComplete,
    decimal EndingBalance,
    bool EndingIsComplete,
    IReadOnlyList<CashFlow> CashFlows,
    IReadOnlyList<BalancePoint> IntermediateBalances,
    Func<DateOnly, PerformanceBalanceResult>? ValueAt = null);

public sealed record CashFlow(DateOnly Date, decimal Amount);

public sealed record BalancePoint(DateOnly Date, decimal Value);
