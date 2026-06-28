using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Api.Tests.Extracts.Fakes;

public sealed class FakeSecuritiesExtractSource : ISecuritiesExtractSource
{
    public Dictionary<string, string> Manifest { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, SecurityExtract?> Securities { get; } = new(StringComparer.Ordinal);

    public List<string> FetchedCiks { get; } = new();

    public Func<string, Exception?>? FailFor { get; set; }

    public Task<SecuritiesManifest> GetManifestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SecuritiesManifest(Manifest));

    public Task<SecurityExtract?> GetSecurityAsync(string cik, CancellationToken cancellationToken = default)
    {
        FetchedCiks.Add(cik);

        var failure = FailFor?.Invoke(cik);
        if (failure is not null)
            throw failure;

        Securities.TryGetValue(cik, out var extract);
        return Task.FromResult(extract);
    }
}
