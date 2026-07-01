namespace Vizfolio.Application.Portfolios;

public interface IAccountHistoryService
{
    Task<HistoryCoverageResult?> GetCoverageAsync(
        Guid portfolioId,
        Guid accountId,
        CancellationToken cancellationToken);

    Task<OpeningBalanceResult?> SetOpeningBalanceAsync(
        Guid portfolioId,
        Guid accountId,
        OpeningBalanceCommand command,
        CancellationToken cancellationToken);
}
