namespace Vizfolio.Infrastructure.Extracts;

public sealed class GitHubExtractOptions
{
    public const string SectionName = "Extracts";

    public ExtractSources Sources { get; set; } = new();

    public ExtractSchedule Schedule { get; set; } = new();

    public string UserAgent { get; set; } = "Vizfolio/1.0";
}

public sealed class ExtractSources
{
    public SecuritiesSourceOptions Securities { get; set; } = new();
    public FundsSourceOptions Funds { get; set; } = new();
}

public sealed class SecuritiesSourceOptions
{
    public string TarballUrl { get; set; } =
        "https://codeload.github.com/vizfolio/securities-extracts/tar.gz/refs/heads/main";
}

public sealed class FundsSourceOptions
{
    public FundsSourceMode Mode { get; set; } = FundsSourceMode.Tarball;

    public string TarballUrl { get; set; } =
        "https://codeload.github.com/vizfolio/fund-extracts/tar.gz/refs/heads/master";

    public string ManifestUrl { get; set; } =
        "https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/funds.json";

    public string SnapshotUrlTemplate { get; set; } =
        "https://raw.githubusercontent.com/vizfolio/fund-extracts/refs/heads/master/snapshots/{seriesId}/{period}.json.gz";

    public int RequestsPerSecond { get; set; } = 5;
}

public enum FundsSourceMode
{
    Tarball,
    PerFile,
}

public sealed class ExtractSchedule
{
    public bool Enabled { get; set; } = true;

    public bool RunOnStartup { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(30);
}
