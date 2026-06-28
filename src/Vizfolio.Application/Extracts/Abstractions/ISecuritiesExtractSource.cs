using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Abstractions;

public interface ISecuritiesExtractSource
{
    Task<SecuritiesManifest> GetManifestAsync(CancellationToken cancellationToken = default);

    Task<SecurityExtract?> GetSecurityAsync(string cik, CancellationToken cancellationToken = default);
}
