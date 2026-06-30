namespace Vizfolio.Application.Performance;

public sealed record PerformanceResult(
    PerformanceScope Scope,
    Guid ScopeId,
    string Currency,
    DateOnly From,
    DateOnly To,
    PerformanceGranularity Granularity,
    PerformanceSummary Summary,
    IReadOnlyList<PerformancePeriod> Series);

public sealed record PerformanceSummary(
    decimal BeginningBalance,
    decimal EndingBalance,
    decimal Deposits,
    decimal Withdrawals,
    decimal NetCashFlow,
    decimal InvestmentReturn,
    RateOfReturnResult RateOfReturn);

public sealed record RateOfReturnResult(
    decimal? ModifiedDietz,
    decimal? Simple,
    decimal? TimeWeighted,
    decimal? MoneyWeighted,
    IReadOnlyList<string> Notes);

public sealed record PerformancePeriod(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Balance,
    decimal Deposits,
    decimal Withdrawals,
    decimal NetCashFlow,
    decimal InvestmentReturn,
    decimal? CumulativeReturnPct);
