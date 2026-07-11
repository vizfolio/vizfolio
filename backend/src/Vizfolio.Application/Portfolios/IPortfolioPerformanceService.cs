namespace Vizfolio.Application.Portfolios;

public interface IPortfolioPerformanceService
{
    Task<PortfolioPerformanceResult?> ComputeForPortfolioAsync(
        Guid portfolioId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task<PortfolioPerformanceResult?> ComputeForAccountAsync(
        Guid portfolioId,
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}
