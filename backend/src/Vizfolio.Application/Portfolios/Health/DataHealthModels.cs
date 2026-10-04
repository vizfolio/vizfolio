namespace Vizfolio.Application.Portfolios.Health;

/// <summary>
/// Everything that makes a portfolio's numbers missing, approximate or assumed — and what to do about it. See
/// docs/performance-api.md → "Data health".
/// </summary>
public interface IDataHealthService
{
    /// <summary>Every account's findings, or null when the portfolio doesn't exist.</summary>
    Task<DataHealthReport?> GetForPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>One account's findings, or null when it isn't in the portfolio.</summary>
    Task<DataHealthReport?> GetForAccountAsync(Guid portfolioId, Guid accountId, CancellationToken cancellationToken);
}

/// <param name="Status">The worst of the accounts' statuses (see <see cref="HealthStatuses"/>).</param>
/// <param name="CurrencyCode">The reporting currency the findings' amounts are in.</param>
public sealed record DataHealthReport(
    string Status, string CurrencyCode, IReadOnlyList<AccountHealth> Accounts, IReadOnlyList<HealthFinding> Findings);

public sealed record AccountHealth(Guid AccountId, string Name, string Status, int Blocking, int Info);

/// <summary>
/// One thing worth knowing about the data, in plain language. <see cref="AccountId"/> is null for a finding about a
/// file that went into several accounts.
/// </summary>
public sealed record HealthFinding(
    string Code,
    string Severity,
    Guid? AccountId,
    Guid? HoldingId,
    string? Symbol,
    DateOnly? From,
    DateOnly? To,
    string Message,
    HealthAction Action,
    HealthDetails Details);

/// <summary>What the user can do about a finding (see <see cref="HealthActionKinds"/>), with a button label.</summary>
public sealed record HealthAction(string Kind, string? Label)
{
    public static readonly HealthAction None = new(HealthActionKinds.None, null);
}

/// <summary>The figures behind a finding; only those relevant to its code are set.</summary>
public sealed record HealthDetails
{
    public static readonly HealthDetails Empty = new();

    public decimal? LedgerQuantity { get; init; }
    public decimal? BrokerQuantity { get; init; }

    /// <summary>The value of a difference, or the total of implied contributions, in the reporting currency.</summary>
    public decimal? Amount { get; init; }

    public int? Count { get; init; }

    /// <summary>For a holding without prices: how the last fetch went (Ok, Empty, Failed, NoSource, AdjustedOnly).</summary>
    public string? PriceFetchOutcome { get; init; }

    public string? PriceFetchMessage { get; init; }

    /// <summary>A price fetch covering the account is queued or running, so this may resolve on its own.</summary>
    public bool PricesPending { get; init; }

    /// <summary>For an import warning: the code the parser reported, example rows and the files.</summary>
    public string? WarningCode { get; init; }

    public IReadOnlyList<string> Samples { get; init; } = [];

    public IReadOnlyList<string> Files { get; init; } = [];
}

public static class HealthStatuses
{
    public const string Healthy = nameof(Healthy);
    public const string Info = nameof(Info);
    public const string NeedsAttention = nameof(NeedsAttention);
}

public static class HealthSeverities
{
    /// <summary>Makes a headline number blank or approximate.</summary>
    public const string Blocking = nameof(Blocking);

    /// <summary>Worth knowing; the numbers are still computed.</summary>
    public const string Info = nameof(Info);
}

public static class HealthCodes
{
    public const string QuantityMismatch = nameof(QuantityMismatch);
    public const string CashMismatch = nameof(CashMismatch);
    public const string NegativePosition = nameof(NegativePosition);
    public const string UnmatchedSplit = nameof(UnmatchedSplit);
    public const string PreHistoryPosition = nameof(PreHistoryPosition);
    public const string UnpricedHolding = nameof(UnpricedHolding);
    public const string StalePrice = nameof(StalePrice);
    public const string UnvaluedTransfer = nameof(UnvaluedTransfer);
    public const string ImportWarning = nameof(ImportWarning);
    public const string ImpliedContributions = nameof(ImpliedContributions);
}

public static class HealthActionKinds
{
    public const string None = nameof(None);
    public const string FetchPrices = nameof(FetchPrices);
    public const string AddPriceProviderKey = nameof(AddPriceProviderKey);
    public const string AdjustStartingPosition = nameof(AdjustStartingPosition);
    public const string ReimportFile = nameof(ReimportFile);
    public const string ReviewImpliedContributions = nameof(ReviewImpliedContributions);
}
