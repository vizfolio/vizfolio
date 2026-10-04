using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Application.PortfolioImports.Abstractions;

public interface IPortfolioImportService
{
    Task<PortfolioImportResult> ImportToAccountAsync(
        Guid accountId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken,
        string? requestedSourceSystem = null);

    Task<PortfolioImportResult> ImportToPortfolioAsync(
        Guid portfolioId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken,
        string? requestedSourceSystem = null);

    /// <summary>
    /// Re-parses stored import files with the current parsers and applies the result: rows a parser used to drop
    /// are added (tagged with their batch), rows it now maps differently are updated (Appendix A.11). Every active
    /// batch with a stored file, oldest first — optionally only one portfolio's, or only the given batches.
    /// </summary>
    Task<ReprocessResult> ReprocessAsync(
        Guid? portfolioId, IReadOnlyCollection<Guid>? batchIds, CancellationToken cancellationToken);
}
