namespace Vizfolio.Application.Extracts.Abstractions;

public interface IHoldingRelinker
{
    Task<int> RelinkAsync(CancellationToken cancellationToken = default);
}
