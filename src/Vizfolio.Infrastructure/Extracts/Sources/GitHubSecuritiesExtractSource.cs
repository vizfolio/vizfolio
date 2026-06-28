using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;

namespace Vizfolio.Infrastructure.Extracts.Sources;

public sealed class GitHubSecuritiesExtractSource : ISecuritiesExtractSource
{
    public const string HttpClientName = "vizfolio.extracts.securities";

    private readonly HttpClient _http;
    private readonly GitHubExtractOptions _options;
    private readonly ILogger<GitHubSecuritiesExtractSource> _logger;

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
        await using var stream = await _http.GetStreamAsync(_options.Sources.Securities.ManifestUrl, cancellationToken);
        var byTicker = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, ExtractsJson.Options, cancellationToken)
            ?? throw new InvalidOperationException("Securities manifest payload was empty.");
        return new SecuritiesManifest(byTicker);
    }

    public async Task<SecurityExtract?> GetSecurityAsync(string cik, CancellationToken cancellationToken = default)
    {
        var url = _options.Sources.Securities.CikUrlTemplate.Replace("{cik}", cik, StringComparison.Ordinal);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Security extract not found for CIK {Cik}", cik);
            return null;
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SecurityExtract>(stream, ExtractsJson.Options, cancellationToken);
    }
}
