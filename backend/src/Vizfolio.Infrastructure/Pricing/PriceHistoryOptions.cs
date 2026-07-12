namespace Vizfolio.Infrastructure.Pricing;

public sealed class PriceHistoryOptions
{
    public const string SectionName = "PriceHistory";

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
}

/// <summary>Keyless default provider. No API key — works out of the box.</summary>
public sealed class StooqProviderOptions
{
    public bool Enabled { get; set; } = true;

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

public sealed class PriceSchedule
{
    public bool Enabled { get; set; } = true;

    public bool RunOnStartup { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(60);
}
