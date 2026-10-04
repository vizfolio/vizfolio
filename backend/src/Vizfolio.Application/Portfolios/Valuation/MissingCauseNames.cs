namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// The cause reported for a value that couldn't be computed. A missing or stale price while a background price fetch
/// for the account is queued or running is <see cref="PricesPending"/> — "prices updating", not "incomplete".
/// </summary>
public static class MissingCauseNames
{
    public const string PricesPending = "PricesPending";

    public static string Of(MissingCause? cause, bool pricesPending)
        => cause is MissingCause.NoPrice or MissingCause.StalePrice && pricesPending
            ? PricesPending
            : cause?.ToString() ?? "Unknown";
}
