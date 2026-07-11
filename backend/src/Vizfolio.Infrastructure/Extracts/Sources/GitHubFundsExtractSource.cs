using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Infrastructure.Extracts.Sources;

public sealed class GitHubFundsExtractSource : IFundsExtractSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly GitHubExtractOptions _options;
    private readonly ILogger<GitHubFundsExtractSource> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private GitHubTarballArchive? _archive;

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
        var archive = await EnsureArchiveAsync(cancellationToken);
        await using var stream = archive.OpenEntry("funds.json")
            ?? throw new InvalidOperationException("Funds tarball is missing funds.json.");
        var manifest = await JsonSerializer.DeserializeAsync<FundsManifest>(stream, ExtractsJson.Options, cancellationToken);
        return manifest ?? throw new InvalidOperationException("Funds manifest payload was empty.");
    }

    public async Task<FundSnapshotExtract?> GetSnapshotAsync(string seriesId, string latestPeriod, CancellationToken cancellationToken = default)
    {
        var archive = await EnsureArchiveAsync(cancellationToken);
        await using var stream = archive.OpenEntry($"snapshots/{seriesId}/{latestPeriod}.json.gz");
        if (stream is null)
        {
            _logger.LogInformation("Fund snapshot not found for {SeriesId} @ {Period}", seriesId, latestPeriod);
            return null;
        }
        await using var gunzipped = new GZipStream(stream, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<FundSnapshotExtract>(gunzipped, ExtractsJson.Options, cancellationToken);
    }

    private async Task<GitHubTarballArchive> EnsureArchiveAsync(CancellationToken cancellationToken)
    {
        if (_archive is not null) return _archive;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_archive is not null) return _archive;
            _logger.LogInformation("Downloading funds extracts tarball from {Url}", _options.Sources.Funds.TarballUrl);
            _archive = await GitHubTarballArchive.DownloadAsync(_http, _options.Sources.Funds.TarballUrl, cancellationToken);
            return _archive;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _archive?.Dispose();
        _initLock.Dispose();
    }
}
