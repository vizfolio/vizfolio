using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Application.Extracts.Abstractions;

/// <summary>Imports the registry of every money market fund (fund-extracts' <c>money_market_funds.json</c>).</summary>
public interface IMoneyMarketFundsImporter
{
    Task<ImportResult> ImportAsync(CancellationToken cancellationToken = default);
}
