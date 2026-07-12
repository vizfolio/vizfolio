using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Pricing.Abstractions;

/// <summary>
/// A pluggable provider of daily close prices (and split events) for a symbol. Register any number of
/// implementations; <see cref="IPriceHistorySourceSelector"/> dispatches to the highest-<see cref="Priority"/>
/// source that <see cref="Supports"/> a request. Mirrors the <c>IPortfolioFileParser</c> plug-in pattern.
/// </summary>
public interface IPriceHistorySource
{
    /// <summary>Provider label stamped onto persisted rows.</summary>
    PriceSource Source { get; }

    /// <summary>Higher wins. Keyless default is lowest; configured API-key providers sit above it.</summary>
    int Priority { get; }

    /// <summary>Whether this source can serve the request (e.g. false when its API key isn't configured).</summary>
    bool Supports(PriceSeriesRequest request);

    /// <summary>Fetch raw/unadjusted daily closes and split events. Returns null when the symbol is unknown.</summary>
    Task<PriceSeriesResult?> GetDailyClosesAsync(PriceSeriesRequest request, CancellationToken cancellationToken = default);
}
