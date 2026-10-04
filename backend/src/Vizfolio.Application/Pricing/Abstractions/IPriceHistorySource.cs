using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Pricing.Abstractions;

/// <summary>
/// A pluggable provider of daily close prices (and split events) for a symbol. Register any number of
/// implementations; <see cref="IPriceHistorySourceSelector"/> dispatches to the highest-<see cref="Priority"/>
/// source that <see cref="Supports"/> a request, falling back down the list when one fails or has no data. Mirrors
/// the <c>IPortfolioFileParser</c> plug-in pattern.
/// </summary>
public interface IPriceHistorySource
{
    /// <summary>Provider label stamped onto persisted rows.</summary>
    PriceSource Source { get; }

    /// <summary>Human-readable name for Settings.</summary>
    string DisplayName { get; }

    /// <summary>Higher wins. Keyless default is lowest; configured API-key providers sit above it.</summary>
    int Priority { get; }

    /// <summary>False when the provider only has split/dividend-adjusted closes, which can't value holdings (Stooq).</summary>
    bool ProvidesRawCloses => true;

    /// <summary>True when the provider needs an API key (settable in Settings or configuration).</summary>
    bool RequiresApiKey { get; }

    /// <summary>Ready to serve requests: enabled, and keyed when it needs a key.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this source can serve the request (e.g. false when its API key isn't configured).</summary>
    bool Supports(PriceSeriesRequest request);

    /// <summary>
    /// Fetch daily closes and split events (raw/as traded unless the result says <see cref="PriceSeriesResult.Adjusted"/>).
    /// Returns null or an empty result when the provider has no data for the symbol; throws when the request fails.
    /// </summary>
    Task<PriceSeriesResult?> GetDailyClosesAsync(PriceSeriesRequest request, CancellationToken cancellationToken = default);
}
