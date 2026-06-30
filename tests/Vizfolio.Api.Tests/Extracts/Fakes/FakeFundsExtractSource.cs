using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Api.Tests.Extracts.Fakes;

public sealed class FakeFundsExtractSource : IFundsExtractSource
{
    public List<FundsManifestEntry> ManifestEntries { get; } = new();

    public Dictionary<string, FundSnapshotExtract?> Snapshots { get; } = new(StringComparer.Ordinal);

    public List<string> FetchedKeys { get; } = new();

    public Task<FundsManifest> GetManifestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new FundsManifest("0.4", DateTimeOffset.UtcNow, ManifestEntries));

    public Task<FundSnapshotExtract?> GetSnapshotAsync(string seriesId, string latestPeriod, CancellationToken cancellationToken = default)
    {
        var key = $"{seriesId}/{latestPeriod}";
        FetchedKeys.Add(key);
        Snapshots.TryGetValue(key, out var snapshot);
        return Task.FromResult(snapshot);
    }
}
