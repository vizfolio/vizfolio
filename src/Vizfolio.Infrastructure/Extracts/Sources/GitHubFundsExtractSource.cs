using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Infrastructure.Extracts.Sources;

public sealed class GitHubFundsExtractSource : IFundsExtractSource
{
    public const string HttpClientName = "vizfolio.extracts.funds";

    private readonly HttpClient _http;
    private readonly GitHubExtractOptions _options;
    private readonly ILogger<GitHubFundsExtractSource> _logger;

    public GitHubFundsExtractSource(
        HttpClient http,
        IOptions<GitHubExtractOptions> options,
        ILogger<GitHubFundsExtractSource> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FundsManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await _http.GetStreamAsync(_options.Sources.Funds.ManifestUrl, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<FundsManifest>(stream, ExtractsJson.Options, cancellationToken);
        return manifest ?? throw new InvalidOperationException("Funds manifest payload was empty.");
    }

    public async Task<FundSnapshotExtract?> GetSnapshotAsync(string seriesId, string latestPeriod, CancellationToken cancellationToken = default)
    {
        var url = _options.Sources.Funds.SnapshotUrlTemplate
            .Replace("{seriesId}", seriesId, StringComparison.Ordinal)
            .Replace("{period}", latestPeriod, StringComparison.Ordinal);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Fund snapshot not found for {SeriesId} @ {Period}", seriesId, latestPeriod);
            return null;
        }
        response.EnsureSuccessStatusCode();

        await using var network = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var gzip = new GZipStream(network, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<FundSnapshotExtract>(gzip, ExtractsJson.Options, cancellationToken);
    }
}
