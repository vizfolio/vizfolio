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
/// Optional EODHD (eodhd.com) price source. Requests the <b>raw/unadjusted</b> <c>close</c> (not
/// <c>adjusted_close</c>) so prices match the raw ledger basis, plus the splits feed so
/// <c>TransactionType.Split</c> can adjust the quantity roll-forward. Disabled (Supports=false) when no
/// API key is configured. Per EODHD's terms this is for the user's own personal use — no redistribution.
/// </summary>
public sealed class EodhdPriceHistorySource : IPriceHistorySource, IDisposable
{
    private readonly HttpClient _http;
    private readonly ApiKeyProviderOptions _options;
    private readonly ILogger<EodhdPriceHistorySource> _logger;
    private readonly RateLimiter _limiter;

    public EodhdPriceHistorySource(
        HttpClient http,
        IOptions<PriceHistoryOptions> options,
        ILogger<EodhdPriceHistorySource> logger)
    {
        _http = http;
        _options = options.Value.Providers.Eodhd;
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

    public PriceSource Source => PriceSource.Eodhd;

    public int Priority => 10;

    public bool Supports(PriceSeriesRequest request)
        => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(request.Symbol);

    public async Task<PriceSeriesResult?> GetDailyClosesAsync(
        PriceSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var symbol = MapSymbol(request.Symbol, request.Exchange);
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var range = $"from={request.From:yyyy-MM-dd}&to={request.To:yyyy-MM-dd}";

        var prices = await GetPricesAsync(baseUrl, symbol, range, cancellationToken);
        if (prices.Count == 0)
        {
            _logger.LogInformation("EODHD returned no daily rows for {Symbol}", symbol);
            return PriceSeriesResult.Empty;
        }

        var splits = await GetSplitsAsync(baseUrl, symbol, range, cancellationToken);
        return new PriceSeriesResult(prices, splits, CurrencyCode: null);
    }

    private async Task<List<PricePoint>> GetPricesAsync(
        string baseUrl, string symbol, string range, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl}/eod/{Uri.EscapeDataString(symbol)}?api_token={_options.ApiKey}&period=d&fmt=json&{range}";
        using var lease = await AcquireAsync(cancellationToken);
        await using var stream = await _http.GetStreamAsync(url, cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var points = new List<PricePoint>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) { return points; }

        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("date", out var dateEl)) { continue; }
            if (!DateOnly.TryParse(dateEl.GetString(), CultureInfo.InvariantCulture, out var date)) { continue; }
            if (!row.TryGetProperty("close", out var closeEl)) { continue; }
            if (!closeEl.TryGetDecimal(out var close)) { continue; }
            points.Add(new PricePoint(date, close));
        }

        return points;
    }

    private async Task<List<SplitEvent>> GetSplitsAsync(
        string baseUrl, string symbol, string range, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl}/splits/{Uri.EscapeDataString(symbol)}?api_token={_options.ApiKey}&fmt=json&{range}";
        var splits = new List<SplitEvent>();
        try
        {
            using var lease = await AcquireAsync(cancellationToken);
            await using var stream = await _http.GetStreamAsync(url, cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return splits; }

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("date", out var dateEl)) { continue; }
                if (!DateOnly.TryParse(dateEl.GetString(), CultureInfo.InvariantCulture, out var exDate)) { continue; }
                if (!row.TryGetProperty("split", out var splitEl)) { continue; }
                if (TryParseSplit(splitEl.GetString(), out var num, out var den))
                    splits.Add(new SplitEvent(exDate, num, den));
            }
        }
        catch (Exception ex)
        {
            // Splits are best-effort: a missing/failed splits feed shouldn't fail the price import.
            _logger.LogWarning(ex, "EODHD splits lookup failed for {Symbol}", symbol);
        }

        return splits;
    }

    /// <summary>EODHD reports splits as a <c>"numerator/denominator"</c> string, e.g. <c>"4.000000/1.000000"</c>.</summary>
    private static bool TryParseSplit(string? raw, out decimal numerator, out decimal denominator)
    {
        numerator = 0m;
        denominator = 0m;
        if (string.IsNullOrWhiteSpace(raw)) { return false; }

        var parts = raw.Split('/');
        if (parts.Length != 2) { return false; }
        if (!decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out numerator)) { return false; }
        if (!decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out denominator)) { return false; }
        return numerator > 0m && denominator > 0m;
    }

    private static string MapSymbol(string symbol, string? exchange)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (normalized.Contains('.')) { return normalized; }
        var market = string.IsNullOrWhiteSpace(exchange) ? "US" : exchange.Trim().ToUpperInvariant();
        return $"{normalized}.{market}";
    }

    private async Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await _limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Could not acquire a rate-limit lease for the EODHD price source.");
        return lease;
    }

    public void Dispose() => _limiter.Dispose();
}
