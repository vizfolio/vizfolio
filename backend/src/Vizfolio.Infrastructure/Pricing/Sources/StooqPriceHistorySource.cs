using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Infrastructure.Pricing.Sources;

/// <summary>
/// Keyless default price source backed by Stooq's public daily CSV endpoint
/// (<c>https://stooq.com/q/d/l/?s=aapl.us&amp;i=d</c>). No API key required, so the app fetches prices
/// out of the box. Note: Stooq's daily series is split/dividend adjusted — treat its closes as
/// best-effort. For the raw/as-traded basis the ledger requires (and clean split events), configure an
/// API-key provider (EODHD / Alpha Vantage), which sit above this source in priority.
/// </summary>
public sealed class StooqPriceHistorySource : IPriceHistorySource, IDisposable
{
    private readonly HttpClient _http;
    private readonly StooqProviderOptions _options;
    private readonly ILogger<StooqPriceHistorySource> _logger;
    private readonly RateLimiter _limiter;

    public StooqPriceHistorySource(
        HttpClient http,
        IOptions<PriceHistoryOptions> options,
        ILogger<StooqPriceHistorySource> logger)
    {
        _http = http;
        _options = options.Value.Providers.Stooq;
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

    public PriceSource Source => PriceSource.Stooq;

    // Lowest priority: the keyless fallback. Configured API-key providers override it.
    public int Priority => 0;

    public bool Supports(PriceSeriesRequest request)
        => _options.Enabled && !string.IsNullOrWhiteSpace(request.Symbol);

    public async Task<PriceSeriesResult?> GetDailyClosesAsync(
        PriceSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var symbol = MapSymbol(request.Symbol, request.Exchange);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/?s={Uri.EscapeDataString(symbol)}" +
                  $"&d1={request.From:yyyyMMdd}&d2={request.To:yyyyMMdd}&i=d";

        using var lease = await AcquireAsync(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        // A browser-like User-Agent reduces (but does not guarantee) Stooq serving its anti-bot page.
        httpRequest.Headers.UserAgent.Clear();
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var csv = await response.Content.ReadAsStringAsync(cancellationToken);

        // Stooq answers automated requests with a 200-OK HTML JavaScript proof-of-work challenge instead
        // of CSV. Detect it and fail loudly — otherwise the HTML parses to zero rows and a *blocked* fetch
        // is indistinguishable from "this symbol has no prices". Reliable fetching needs an API-key provider.
        if (LooksLikeChallenge(csv))
        {
            throw new InvalidOperationException(
                $"Stooq returned an anti-bot challenge page for '{symbol}' instead of CSV. The keyless " +
                "Stooq endpoint is gating automated requests; configure an API-key price provider " +
                "(PriceHistory:Providers:Eodhd or :AlphaVantage) for reliable fetching.");
        }

        var prices = ParseCsv(csv);
        if (prices.Count == 0)
        {
            _logger.LogInformation("Stooq returned no daily rows for {Symbol}", symbol);
            return PriceSeriesResult.Empty;
        }

        // Stooq's CSV daily endpoint carries no split feed; splits come from API-key providers.
        return new PriceSeriesResult(prices, Array.Empty<SplitEvent>(), CurrencyCode: null);
    }

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    /// <summary>True when the response body is Stooq's HTML anti-bot page rather than CSV.</summary>
    private static bool LooksLikeChallenge(string body)
    {
        var trimmed = body.TrimStart();
        return trimmed.StartsWith("<", StringComparison.Ordinal)
            || body.Contains("requires JavaScript", StringComparison.OrdinalIgnoreCase)
            || body.Contains("__verify", StringComparison.OrdinalIgnoreCase);
    }

    private static List<PricePoint> ParseCsv(string csv)
    {
        var points = new List<PricePoint>();
        using var reader = new StringReader(csv);
        string? line;
        var header = true;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) { continue; }
            if (header)
            {
                header = false;
                if (line.StartsWith("Date", StringComparison.OrdinalIgnoreCase)) { continue; }
            }

            var cells = line.Split(',');
            if (cells.Length < 5) { continue; }
            if (!DateOnly.TryParse(cells[0], CultureInfo.InvariantCulture, out var date)) { continue; }
            if (!decimal.TryParse(cells[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var close)) { continue; }

            points.Add(new PricePoint(date, close));
        }

        return points;
    }

    /// <summary>Stooq symbols are lowercase with a market suffix (e.g. <c>aapl.us</c>). Default to US.</summary>
    private static string MapSymbol(string symbol, string? exchange)
    {
        var normalized = symbol.Trim().ToLowerInvariant();
        if (normalized.Contains('.')) { return normalized; }
        return $"{normalized}.us";
    }

    private async Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await _limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Could not acquire a rate-limit lease for the Stooq price source.");
        return lease;
    }

    public void Dispose() => _limiter.Dispose();
}
