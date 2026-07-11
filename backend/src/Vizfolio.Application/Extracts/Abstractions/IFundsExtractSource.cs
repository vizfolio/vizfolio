using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Abstractions;

public interface IFundsExtractSource
{
    Task<FundsManifest> GetManifestAsync(CancellationToken cancellationToken = default);

    Task<FundSnapshotExtract?> GetSnapshotAsync(string seriesId, string latestPeriod, CancellationToken cancellationToken = default);
}
