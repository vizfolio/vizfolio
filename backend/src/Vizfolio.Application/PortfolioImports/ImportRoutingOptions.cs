namespace Vizfolio.Application.PortfolioImports;

/// <summary>
/// How an upload finds its account when the file doesn't say which one it is (e.g. the Vanguard transaction report),
/// bound from the <c>Imports:Routing</c> configuration section. The file's rows are fingerprinted against each
/// account's ledger; an account that already holds many of them is where the file belongs. See
/// docs/performance-api.md → "Importing files".
/// </summary>
public sealed class ImportRoutingOptions
{
    public const string SectionName = "Imports:Routing";

    /// <summary>
    /// <see cref="ImportRoutingMode.Auto"/> imports straight into a clearly matching account;
    /// <see cref="ImportRoutingMode.Confirm"/> always asks, with the best match pre-selected.
    /// </summary>
    public ImportRoutingMode Mode { get; set; } = ImportRoutingMode.Auto;

    /// <summary>The best account must already hold at least this many of the file's transactions.</summary>
    public int MinMatchingRows { get; set; } = 5;

    /// <summary>…and at least this many times as many as the runner-up (an account with none always loses).</summary>
    public decimal MinLeadRatio { get; set; } = 2m;

    /// <summary>
    /// …and this share of the file's rows dated within the account's existing history must be ones it already holds.
    /// A re-export of the same history matches nearly all of them; a file that overlaps the account's dates but
    /// shares only a handful of coincidentally identical rows belongs elsewhere.
    /// </summary>
    public decimal MinOverlapShare { get; set; } = 0.5m;

    /// <summary>
    /// When a file is uploaded to one account but clearly belongs to another, stop and ask instead of importing.
    /// </summary>
    public bool GuardAccountImports { get; set; } = true;
}

public enum ImportRoutingMode
{
    Auto,
    Confirm,
}
