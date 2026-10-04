namespace Vizfolio.Infrastructure.Pricing;

public sealed class PriceHistoryOptions
{
    public const string SectionName = "PriceHistory";

    /// <summary>Fetch prices in the background right after an import, for the accounts it touched.</summary>
    public bool RefreshOnImport { get; set; } = true;

    public PriceSchedule Schedule { get; set; } = new();

    public PriceProviders Providers { get; set; } = new();

    public string UserAgent { get; set; } = "Vizfolio/1.0";
}

public sealed class PriceProviders
{
    public StooqProviderOptions Stooq { get; set; } = new();

    public ApiKeyProviderOptions Eodhd { get; set; } = new()
    {
        BaseUrl = "https://eodhd.com/api",
    };

    public ApiKeyProviderOptions AlphaVantage { get; set; } = new()
    {
        BaseUrl = "https://www.alphavantage.co/query",
    };

    public TiingoProviderOptions Tiingo { get; set; } = new();
}

/// <summary>
/// Keyless provider, off by default: its closes are split/dividend adjusted (stored, but never used for valuation).
/// </summary>
public sealed class StooqProviderOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://stooq.com/q/d/l/";

    public int RequestsPerSecond { get; set; } = 5;
}

/// <summary>An optional API-key provider. Blank <see cref="ApiKey"/> disables it (Supports returns false).</summary>
public sealed class ApiKeyProviderOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public int RequestsPerSecond { get; set; } = 5;
}

/// <summary>
/// Tiingo (api.tiingo.com). Blank <see cref="ApiKey"/> disables it. The free tier is metered per hour
/// (50 requests/hour, 1,000/day, 500 unique symbols/month), so the limiter is hourly.
/// </summary>
public sealed class TiingoProviderOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.tiingo.com";

    public int RequestsPerHour { get; set; } = 50;
}

/// <summary>
/// The scheduled refresh: a catch-up shortly after startup, then daily at <see cref="DailyAt"/> in
/// <see cref="TimeZone"/> — after the US close, once mutual fund NAVs are published. Without <see cref="DailyAt"/>,
/// every <see cref="Interval"/>.
/// </summary>
public sealed class PriceSchedule
{
    public bool Enabled { get; set; } = true;

    public bool RunOnStartup { get; set; } = true;

    /// <summary>Local time of day for the daily refresh (in <see cref="TimeZone"/>); null to use <see cref="Interval"/>.</summary>
    public TimeSpan? DailyAt { get; set; } = new TimeSpan(20, 0, 0);

    /// <summary>IANA (or Windows) time zone id for <see cref="DailyAt"/>.</summary>
    public string TimeZone { get; set; } = "America/New_York";

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(60);
}
