using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Application.PortfolioImports.Abstractions;

public interface IPortfolioImportService
{
    Task<PortfolioImportResult> ImportToAccountAsync(
        Guid accountId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken);

    Task<PortfolioImportResult> ImportToPortfolioAsync(
        Guid portfolioId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken);
}
