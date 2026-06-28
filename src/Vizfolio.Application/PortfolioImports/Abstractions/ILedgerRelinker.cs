namespace Vizfolio.Application.PortfolioImports.Abstractions;

public interface ILedgerRelinker
{
    Task<int> RelinkAsync(CancellationToken cancellationToken = default);
}
