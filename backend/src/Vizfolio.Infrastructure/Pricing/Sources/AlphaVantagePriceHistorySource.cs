using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Infrastructure.Pricing.Sources;

/// <summary>
/// Optional Alpha Vantage (alphavantage.co) price source. Uses <c>TIME_SERIES_DAILY</c>, whose closes are
/// <b>raw/unadjusted</b> — matching the ledger basis. That endpoint carries no split feed (split
/// coefficients live in the premium <c>TIME_SERIES_DAILY_ADJUSTED</c>), so splits come back empty; the
/// roll-forward still applies imported <c>Split</c> ledger rows. Disabled when no API key is configured.
/// Note Alpha Vantage's free tier is personal, non-commercial use only (25 requests/day).
/// </summary>
public sealed class AlphaVantagePriceHistorySource : IPriceHistorySource, IDisposable
{
    private readonly HttpClient _http;
    private readonly ApiKeyProviderOptions _options;
    private readonly ILogger<AlphaVantagePriceHistorySource> _logger;
    private readonly RateLimiter _limiter;

    public AlphaVantagePriceHistorySource(
        HttpClient http,
        IOptions<PriceHistoryOptions> options,
        ILogger<AlphaVantagePriceHistorySource> logger)
    {
        _http = http;
        _options = options.Value.Providers.AlphaVantage;
        _logger = logger;

        var perSecond = Math.Max(1, _options.RequestsPerSecond);
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perSecond,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    public PriceSource Source => PriceSource.AlphaVantage;

    public int Priority => 20;

    public bool Supports(PriceSeriesRequest request)
        => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(request.Symbol);

    public async Task<PriceSeriesResult?> GetDailyClosesAsync(
        PriceSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var url = $"{_options.BaseUrl.TrimEnd('/')}?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(symbol)}" +
                  $"&outputsize=full&apikey={_options.ApiKey}";

        using var lease = await AcquireAsync(cancellationToken);
        await using var stream = await _http.GetStreamAsync(url, cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("Time Series (Daily)", out var series)
            || series.ValueKind != JsonValueKind.Object)
        {
            _logger.LogInformation("Alpha Vantage returned no daily series for {Symbol}", symbol);
            return PriceSeriesResult.Empty;
        }

        var prices = new List<PricePoint>();
        foreach (var day in series.EnumerateObject())
        {
            if (!DateOnly.TryParse(day.Name, CultureInfo.InvariantCulture, out var date)) { continue; }
            if (date < request.From || date > request.To) { continue; }
            if (!day.Value.TryGetProperty("4. close", out var closeEl)) { continue; }
            if (!decimal.TryParse(closeEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var close)) { continue; }
            prices.Add(new PricePoint(date, close));
        }

        return new PriceSeriesResult(prices, Array.Empty<SplitEvent>(), CurrencyCode: null);
    }

    private async Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await _limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Could not acquire a rate-limit lease for the Alpha Vantage price source.");
        return lease;
    }

    public void Dispose() => _limiter.Dispose();
}
