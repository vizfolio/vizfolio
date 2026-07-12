using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Tests.Pricing;

internal sealed class FakePriceHistorySource : IPriceHistorySource
{
    public PriceSource Source { get; init; } = PriceSource.Stooq;

    public int Priority { get; init; }

    public bool Enabled { get; init; } = true;

    public Func<PriceSeriesRequest, PriceSeriesResult?>? Handler { get; init; }

    public List<PriceSeriesRequest> Requests { get; } = new();

    public bool Supports(PriceSeriesRequest request) => Enabled;

    public Task<PriceSeriesResult?> GetDailyClosesAsync(
        PriceSeriesRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult<PriceSeriesResult?>(Handler?.Invoke(request) ?? PriceSeriesResult.Empty);
    }
}
