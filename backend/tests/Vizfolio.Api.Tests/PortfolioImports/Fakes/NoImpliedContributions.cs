using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Fakes;

/// <summary>For tests about ingesting imported rows: records no implied contributions.</summary>
public sealed class NoImpliedContributions : IImpliedContributionService
{
    public static readonly NoImpliedContributions Instance = new();

    public Task<ImpliedContributionPreview?> PreviewForAccountAsync(
        Guid portfolioId, Guid accountId, CancellationToken cancellationToken)
        => Task.FromResult<ImpliedContributionPreview?>(null);

    public Task<ImpliedContributionSyncResult> SyncForAccountAsync(Guid accountId, CancellationToken cancellationToken)
        => Task.FromResult(new ImpliedContributionSyncResult(0, 0m));
}
