using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Abstractions;

public interface IFundsImporter
{
    Task<ImportResult> ImportAsync(FundsImportOptions options, CancellationToken cancellationToken = default);
}
