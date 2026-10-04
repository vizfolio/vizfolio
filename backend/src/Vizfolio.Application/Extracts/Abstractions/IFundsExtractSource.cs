using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Abstractions;

public interface IFundsExtractSource
{
    Task<FundsManifest> GetManifestAsync(CancellationToken cancellationToken = default);

    Task<FundSnapshotExtract?> GetSnapshotAsync(string seriesId, string latestPeriod, CancellationToken cancellationToken = default);

    /// <summary>The registry of every money market fund (<c>money_market_funds.json</c>); null when not published.</summary>
    Task<MoneyMarketRegistryExtract?> GetMoneyMarketRegistryAsync(CancellationToken cancellationToken = default);
}
