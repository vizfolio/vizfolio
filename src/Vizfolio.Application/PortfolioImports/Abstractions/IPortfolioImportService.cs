using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Application.PortfolioImports.Abstractions;

public interface IPortfolioImportService
{
    Task<PortfolioImportResult> ImportAsync(
        Guid accountId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken);
}
