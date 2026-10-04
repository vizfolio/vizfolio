namespace Vizfolio.Application.Pricing.Abstractions;

/// <summary>
/// Background price fetching. Callers enqueue what needs prices — after an import, the accounts it touched; on a
/// schedule or from Settings, everything — and a single worker fetches them one run at a time, never overlapping an
/// extracts import. Enqueuing returns immediately.
/// </summary>
public interface IPriceRefreshQueue
{
    void Enqueue(PriceRefreshRequest request);
}

/// <summary>What to fetch prices for: the series held in <see cref="AccountIds"/>, or every series when null.</summary>
public sealed record PriceRefreshRequest(IReadOnlyCollection<Guid>? AccountIds, PriceRefreshTrigger Trigger)
{
    public static PriceRefreshRequest Everything(PriceRefreshTrigger trigger) => new(null, trigger);

    public static PriceRefreshRequest ForAccounts(IEnumerable<Guid> accountIds, PriceRefreshTrigger trigger)
        => new(accountIds.Distinct().ToList(), trigger);
}

public enum PriceRefreshTrigger
{
    /// <summary>A file was imported (or an import undone or reprocessed).</summary>
    Import,

    /// <summary>The startup catch-up or the daily refresh.</summary>
    Schedule,

    /// <summary>Asked for from Settings (or after a provider key changed).</summary>
    Manual,
}

/// <summary>Whether prices are being fetched, so the UI can say "Prices updating…" instead of "incomplete".</summary>
public interface IPriceRefreshStatus
{
    /// <summary>True while a refresh covering the account is queued or running.</summary>
    bool IsPending(Guid accountId);

    PriceRefreshState Current { get; }
}

/// <summary>The background price refresh, as Settings shows it.</summary>
public sealed record PriceRefreshState(
    bool Running,
    bool Pending,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastFinishedAt,
    // "Import", "Schedule" or "Manual".
    string? LastTrigger,
    int? LastUpserted,
    int? LastFailed,
    string? LastError);
