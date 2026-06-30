using Vizfolio.Application.Performance;

namespace Vizfolio.Api.Endpoints.Portfolios.Performance;

public sealed record PerformanceResponse(
    PerformanceScope Scope,
    Guid ScopeId,
    string Currency,
    DateOnly From,
    DateOnly To,
    PerformanceGranularity Granularity,
    PerformanceSummaryResponse Summary,
    IReadOnlyList<PerformancePeriodResponse> Series);

public sealed record PerformanceSummaryResponse(
    decimal BeginningBalance,
    decimal EndingBalance,
    decimal Deposits,
    decimal Withdrawals,
    decimal NetCashFlow,
    decimal InvestmentReturn,
    RateOfReturnResponse RateOfReturn);

public sealed record RateOfReturnResponse(
    decimal? ModifiedDietz,
    decimal? Simple,
    decimal? TimeWeighted,
    decimal? MoneyWeighted,
    IReadOnlyList<string> Notes);

public sealed record PerformancePeriodResponse(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Balance,
    decimal Deposits,
    decimal Withdrawals,
    decimal NetCashFlow,
    decimal InvestmentReturn,
    decimal? CumulativeReturnPct);

internal static class PerformanceResponseMapper
{
    public static PerformanceResponse ToResponse(this PerformanceResult result) =>
        new(
            result.Scope,
            result.ScopeId,
            result.Currency,
            result.From,
            result.To,
            result.Granularity,
            new PerformanceSummaryResponse(
                result.Summary.BeginningBalance,
                result.Summary.EndingBalance,
                result.Summary.Deposits,
                result.Summary.Withdrawals,
                result.Summary.NetCashFlow,
                result.Summary.InvestmentReturn,
                new RateOfReturnResponse(
                    result.Summary.RateOfReturn.ModifiedDietz,
                    result.Summary.RateOfReturn.Simple,
                    result.Summary.RateOfReturn.TimeWeighted,
                    result.Summary.RateOfReturn.MoneyWeighted,
                    result.Summary.RateOfReturn.Notes)),
            result.Series
                .Select(p => new PerformancePeriodResponse(
                    p.PeriodStart, p.PeriodEnd, p.Balance,
                    p.Deposits, p.Withdrawals, p.NetCashFlow,
                    p.InvestmentReturn, p.CumulativeReturnPct))
                .ToList());
}
