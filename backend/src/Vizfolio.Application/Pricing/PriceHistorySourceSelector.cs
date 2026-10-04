using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;

namespace Vizfolio.Application.Pricing;

public sealed class PriceHistorySourceSelector : IPriceHistorySourceSelector
{
    private readonly IReadOnlyList<IPriceHistorySource> _sources;

    public PriceHistorySourceSelector(IEnumerable<IPriceHistorySource> sources)
        => _sources = sources.OrderByDescending(s => s.Priority).ToList();

    public IReadOnlyList<IPriceHistorySource> All => _sources;

    public IPriceHistorySource? Select(PriceSeriesRequest request)
        => _sources.FirstOrDefault(s => s.Supports(request));

    public IReadOnlyList<IPriceHistorySource> SelectAll(PriceSeriesRequest request)
        => _sources.Where(s => s.Supports(request)).ToList();
}
