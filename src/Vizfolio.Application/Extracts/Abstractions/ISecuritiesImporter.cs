using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Abstractions;

public interface ISecuritiesImporter
{
    Task<ImportResult> ImportAsync(SecuritiesImportOptions options, CancellationToken cancellationToken = default);
}
