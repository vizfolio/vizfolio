using Vizfolio.Application.Pricing.Models;

namespace Vizfolio.Application.Pricing.Abstractions;

public interface IPriceHistorySourceSelector
{
    /// <summary>The highest-priority source that supports the request, or null if none do.</summary>
    IPriceHistorySource? Select(PriceSeriesRequest request);

    /// <summary>Every source that supports the request, highest priority first — the fallback chain.</summary>
    IReadOnlyList<IPriceHistorySource> SelectAll(PriceSeriesRequest request);

    /// <summary>All registered sources, highest priority first (available or not).</summary>
    IReadOnlyList<IPriceHistorySource> All { get; }
}
