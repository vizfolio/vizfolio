namespace Vizfolio.Application.Portfolios;

public sealed record PerformanceComputationContext(
    DateOnly From,
    DateOnly To,
    decimal StartingBalance,
    bool StartingIsComplete,
    decimal EndingBalance,
    bool EndingIsComplete,
    IReadOnlyList<CashFlow> CashFlows,
    IReadOnlyList<BalancePoint> IntermediateBalances);

public sealed record CashFlow(DateOnly Date, decimal Amount);

public sealed record BalancePoint(DateOnly Date, decimal Value);
