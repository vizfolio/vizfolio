using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Infrastructure.Pricing.Sources;

/// <summary>
/// Optional Tiingo (tiingo.com) price source — the preferred provider when a key is configured. One call
/// per symbol returns the <b>raw/unadjusted</b> <c>close</c> (which matches the raw ledger basis; for mutual
/// funds it is the daily NAV) together with the day's <c>splitFactor</c>, so prices and split events come
/// from the same series. Covers US stocks, ETFs and mutual funds.
/// <para>
/// The key is sent in the <c>Authorization</c> header, never the URL, so it can't leak into request logs.
/// Disabled (Supports=false) when no key is configured. The free tier is "internal use only": the user's
/// own personal use, no display or sharing with others — which matches the per-instance local fetch.
/// </para>
/// </summary>
public sealed class TiingoPriceHistorySource : IPriceHistorySource, IDisposable
{
    private readonly HttpClient _http;
    private readonly TiingoProviderOptions _options;
    private readonly ILogger<TiingoPriceHistorySource> _logger;
    private readonly RateLimiter _limiter;

    public TiingoPriceHistorySource(
        HttpClient http,
        IOptions<PriceHistoryOptions> options,
        ILogger<TiingoPriceHistorySource> logger)
    {
        _http = http;
        _options = options.Value.Providers.Tiingo;
        _logger = logger;

        var perHour = Math.Max(1, _options.RequestsPerHour);
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perHour,
            TokensPerPeriod = perHour,
            ReplenishmentPeriod = TimeSpan.FromHours(1),
            AutoReplenishment = true,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    public PriceSource Source => PriceSource.Tiingo;

    // Highest priority: raw closes, splits and mutual-fund NAVs in a single call.
    public int Priority => 30;

    public bool Supports(PriceSeriesRequest request)
        => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(request.Symbol);

    public async Task<PriceSeriesResult?> GetDailyClosesAsync(
        PriceSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var symbol = MapSymbol(request.Symbol);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/tiingo/daily/{Uri.EscapeDataString(symbol)}/prices" +
                  $"?startDate={request.From:yyyy-MM-dd}&endDate={request.To:yyyy-MM-dd}&format=json";

        using var lease = await AcquireAsync(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.ApiKey}");
        using var response = await _http.SendAsync(httpRequest, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Tiingo has no daily series for {Symbol}", symbol);
            return PriceSeriesResult.Empty;
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Tiingo returned an unexpected payload for {Symbol}", symbol);
            return PriceSeriesResult.Empty;
        }

        var prices = new List<PricePoint>();
        var splits = new List<SplitEvent>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!TryReadDate(row, out var date)) { continue; }
            if (!row.TryGetProperty("close", out var closeEl) || !closeEl.TryGetDecimal(out var close)) { continue; }
            prices.Add(new PricePoint(date, close));

            // splitFactor is 1 on ordinary days; anything else is a split effective that day (ex-date),
            // e.g. 4 for a 4-for-1 split, 0.1 for a 1-for-10 reverse split.
            if (row.TryGetProperty("splitFactor", out var splitEl)
                && splitEl.TryGetDecimal(out var factor)
                && factor > 0m && factor != 1m)
                splits.Add(new SplitEvent(date, factor, 1m));
        }

        if (prices.Count == 0)
            _logger.LogInformation("Tiingo returned no daily rows for {Symbol}", symbol);

        return new PriceSeriesResult(prices, splits, CurrencyCode: null);
    }

    /// <summary>Tiingo dates are ISO timestamps at midnight UTC (<c>2019-01-02T00:00:00.000Z</c>); keep the day.</summary>
    private static bool TryReadDate(JsonElement row, out DateOnly date)
    {
        date = default;
        if (!row.TryGetProperty("date", out var dateEl)) { return false; }
        var raw = dateEl.GetString();
        return raw is { Length: >= 10 }
            && DateOnly.TryParseExact(raw[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Tiingo writes share classes with a hyphen (<c>BRK-B</c>), where brokers often use a dot or slash.</summary>
    private static string MapSymbol(string symbol)
        => symbol.Trim().ToUpperInvariant().Replace('.', '-').Replace('/', '-');

    private async Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await _limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Could not acquire a rate-limit lease for the Tiingo price source.");
        return lease;
    }

    public void Dispose() => _limiter.Dispose();
}
