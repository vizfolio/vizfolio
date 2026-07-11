using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Infrastructure.Extracts.Sources;

public sealed class GitHubSecuritiesExtractSource : ISecuritiesExtractSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly GitHubExtractOptions _options;
    private readonly ILogger<GitHubSecuritiesExtractSource> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private GitHubTarballArchive? _archive;

    public GitHubSecuritiesExtractSource(
        HttpClient http,
        IOptions<GitHubExtractOptions> options,
        ILogger<GitHubSecuritiesExtractSource> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SecuritiesManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        var archive = await EnsureArchiveAsync(cancellationToken);
        await using var stream = archive.OpenEntry("by_ticker.json")
            ?? throw new InvalidOperationException("Securities tarball is missing by_ticker.json.");
        var byTicker = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, ExtractsJson.Options, cancellationToken)
            ?? throw new InvalidOperationException("Securities manifest payload was empty.");
        return new SecuritiesManifest(byTicker);
    }

    public async Task<SecurityExtract?> GetSecurityAsync(string cik, CancellationToken cancellationToken = default)
    {
        var archive = await EnsureArchiveAsync(cancellationToken);
        await using var stream = archive.OpenEntry($"by_cik/{cik}.json");
        if (stream is null)
        {
            _logger.LogInformation("Security extract not found for CIK {Cik}", cik);
            return null;
        }
        return await JsonSerializer.DeserializeAsync<SecurityExtract>(stream, ExtractsJson.Options, cancellationToken);
    }

    private async Task<GitHubTarballArchive> EnsureArchiveAsync(CancellationToken cancellationToken)
    {
        if (_archive is not null) return _archive;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_archive is not null) return _archive;
            _logger.LogInformation("Downloading securities extracts tarball from {Url}", _options.Sources.Securities.TarballUrl);
            _archive = await GitHubTarballArchive.DownloadAsync(_http, _options.Sources.Securities.TarballUrl, cancellationToken);
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
