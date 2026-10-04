using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.Pricing.Models;

namespace Vizfolio.Application.Pricing.Abstractions;

public interface IPriceHistoryImporter
{
    Task<ImportResult> ImportAsync(PriceHistoryImportOptions options, CancellationToken cancellationToken = default);
}
