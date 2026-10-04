namespace Vizfolio.Domain.Pricing;

/// <summary>
/// How fetching one price series last went: when, from which provider, with what outcome, and which raw closes are
/// stored. Feeds the price status endpoint (and Data Health) so a symbol no provider has, or one whose history
/// starts later than needed, is visible rather than silently unvalued. One row per series — keyed exactly like
/// <see cref="PriceHistory"/> (a Security, or an uppercased symbol), enforced by the importer.
/// </summary>
public sealed class PriceSeriesStatus
{
    private PriceSeriesStatus() { }

    private PriceSeriesStatus(PriceSeriesKind kind, Guid? securityId, string? symbolKey, string querySymbol)
    {
        PriceSeriesStatusId = Guid.NewGuid();
        Kind = kind;
        SecurityId = securityId;
        SymbolKey = symbolKey;
        QuerySymbol = querySymbol.Trim().ToUpperInvariant();
    }

    public Guid PriceSeriesStatusId { get; private set; }

    public PriceSeriesKind Kind { get; private set; }

    public Guid? SecurityId { get; private set; }

    public string? SymbolKey { get; private set; }

    /// <summary>The ticker sent to providers.</summary>
    public string QuerySymbol { get; private set; } = string.Empty;

    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>The provider that answered the last attempt (the first in the fallback chain that had data).</summary>
    public PriceSource? LastSource { get; private set; }

    public PriceFetchOutcome LastOutcome { get; private set; }

    /// <summary>Why the last attempt failed or came back empty, for display.</summary>
    public string? Message { get; private set; }

    /// <summary>The earliest date the series is needed from (a week before the first account holding it starts).</summary>
    public DateOnly? NeededFrom { get; private set; }

    /// <summary>Earliest stored raw close.</summary>
    public DateOnly? FirstStored { get; private set; }

    /// <summary>Latest stored raw close.</summary>
    public DateOnly? LastStored { get; private set; }

    /// <summary>
    /// Every provider was asked for closes before <see cref="FirstStored"/> and had none: the history starts here (e.g.
    /// the fund launched later, or the provider's data does). Older closes aren't asked for again until this changes.
    /// </summary>
    public DateOnly? NoDataBefore { get; private set; }

    public static PriceSeriesStatus ForSecurity(Guid securityId, string querySymbol)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));
        return new PriceSeriesStatus(PriceSeriesKind.Security, securityId, null, querySymbol);
    }

    public static PriceSeriesStatus ForSymbol(string symbolKey, string querySymbol)
    {
        var normalized = PriceHistory.NormalizeSymbol(symbolKey)
                         ?? throw new ArgumentException("Symbol key is required.", nameof(symbolKey));
        return new PriceSeriesStatus(PriceSeriesKind.Symbol, null, normalized, querySymbol);
    }

    public void RecordAttempt(
        PriceFetchOutcome outcome,
        PriceSource? source,
        string? message,
        DateOnly? neededFrom,
        DateOnly? firstStored,
        DateOnly? lastStored,
        DateTimeOffset? attemptedAt = null)
    {
        NeededFrom = neededFrom;
        LastAttemptAt = attemptedAt ?? DateTimeOffset.UtcNow;
        LastOutcome = outcome;
        LastSource = source;
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        FirstStored = firstStored;
        LastStored = lastStored;
    }

    public void MarkNoDataBefore(DateOnly? date) => NoDataBefore = date;
}

/// <summary>How a price series fetch went.</summary>
public enum PriceFetchOutcome
{
    /// <summary>Raw closes are stored (possibly nothing new — the series was already up to date).</summary>
    Ok,

    /// <summary>Every provider that supports the symbol answered with no data ("no data from provider").</summary>
    Empty,

    /// <summary>Every provider failed (error, rate limit, network).</summary>
    Failed,

    /// <summary>No configured provider supports the symbol (e.g. no API key set).</summary>
    NoSource,

    /// <summary>Only split/dividend-adjusted closes were available, which valuation can't use.</summary>
    AdjustedOnly,
}
