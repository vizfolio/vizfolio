using Vizfolio.Application.Pricing.Models;

namespace Vizfolio.Application.Pricing.Abstractions;

public interface IPriceHistorySourceSelector
{
    /// <summary>The highest-priority source that supports the request, or null if none do.</summary>
    IPriceHistorySource? Select(PriceSeriesRequest request);
}
