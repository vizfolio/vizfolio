using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Pricing;

internal sealed class FakePriceHistorySource : IPriceHistorySource
{
    public PriceSource Source { get; init; } = PriceSource.Stooq;

    public int Priority { get; init; }

    public bool Enabled { get; init; } = true;

    public string DisplayName => Source.ToString();

    public bool RequiresApiKey => false;

    public bool IsAvailable => Enabled;

    public Func<PriceSeriesRequest, PriceSeriesResult?>? Handler { get; init; }

    /// <summary>Thrown instead of answering, to simulate a failing provider (rate limit, network).</summary>
    public Exception? Throws { get; init; }

    public List<PriceSeriesRequest> Requests { get; } = new();

    public bool Supports(PriceSeriesRequest request) => Enabled;

    public Task<PriceSeriesResult?> GetDailyClosesAsync(
        PriceSeriesRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Throws is not null) throw Throws;
        // Like a real provider, only return closes inside the requested range.
        var result = Handler?.Invoke(request) ?? PriceSeriesResult.Empty;
        return Task.FromResult<PriceSeriesResult?>(result with
        {
            Prices = result.Prices.Where(p => p.AsOf >= request.From && p.AsOf <= request.To).ToList(),
        });
    }
}
