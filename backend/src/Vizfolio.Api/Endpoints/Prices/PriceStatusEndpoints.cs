using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Api.Endpoints.Prices;

/// <summary>One price series and how fetching it last went.</summary>
public sealed record PriceSeriesStatusResponse(
    string Symbol,
    string Kind,
    DateTimeOffset? LastAttemptAt,
    string? LastSource,
    string LastOutcome,
    string? Message,
    DateOnly? NeededFrom,
    DateOnly? FirstStored,
    DateOnly? LastStored,
    DateOnly? NoDataBefore);

/// <summary>The background price refresh and every series' coverage.</summary>
public sealed record PriceStatusResponse(
    PriceRefreshState Refresh,
    int ProvidersAvailable,
    IReadOnlyList<PriceSeriesStatusResponse> Series);

/// <summary>GET /api/prices/status.</summary>
public sealed class GetPriceStatusEndpoint : EndpointWithoutRequest<PriceStatusResponse>
{
    private readonly IAppDbContext _db;
    private readonly IPriceRefreshStatus _refresh;
    private readonly IPriceHistorySourceSelector _sources;
    private readonly IProviderKeyStore _keys;

    public GetPriceStatusEndpoint(
        IAppDbContext db, IPriceRefreshStatus refresh, IPriceHistorySourceSelector sources, IProviderKeyStore keys)
    {
        _db = db;
        _refresh = refresh;
        _sources = sources;
        _keys = keys;
    }

    public override void Configure()
    {
        Get("/prices/status");
        AllowAnonymous();
        Description(b => b.WithTags("Prices").Produces<PriceStatusResponse>(StatusCodes.Status200OK));
        Summary(s =>
        {
            s.Summary = "Price fetching status: the background refresh, and each price series' coverage and last outcome.";
            s.Description =
                "`refresh` says whether a fetch is running or queued (imports queue one for the accounts they touch) and how the last run went. " +
                "`series` lists each price series, problems first: `lastOutcome` is `Ok`, `Empty` (no data from any provider), `Failed`, " +
                "`NoSource` (no provider set up — add an API key) or `AdjustedOnly`; `neededFrom` vs `firstStored` shows whether the stored " +
                "history reaches back far enough, and `noDataBefore` where providers' history starts. `providersAvailable` counts providers " +
                "ready to fetch raw prices.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await _keys.EnsureLoadedAsync(ct);
        var rows = await _db.PriceSeriesStatuses.AsNoTracking().ToListAsync(ct);
        var series = rows
            .OrderBy(r => r.LastOutcome == PriceFetchOutcome.Ok ? 1 : 0)
            .ThenBy(r => r.QuerySymbol, StringComparer.Ordinal)
            .Select(r => new PriceSeriesStatusResponse(
                r.QuerySymbol, r.Kind.ToString(), r.LastAttemptAt, r.LastSource?.ToString(), r.LastOutcome.ToString(),
                r.Message, r.NeededFrom, r.FirstStored, r.LastStored, r.NoDataBefore))
            .ToList();

        await Send.OkAsync(new PriceStatusResponse(
            _refresh.Current, _sources.All.Count(s => s.IsAvailable && s.ProvidesRawCloses), series), ct);
    }
}

/// <summary>POST /api/prices/refresh — queue a fetch of every series.</summary>
public sealed class RefreshPricesEndpoint : EndpointWithoutRequest<PriceRefreshState>
{
    private readonly IPriceRefreshQueue _queue;
    private readonly IPriceRefreshStatus _status;

    public RefreshPricesEndpoint(IPriceRefreshQueue queue, IPriceRefreshStatus status)
    {
        _queue = queue;
        _status = status;
    }

    public override void Configure()
    {
        Post("/prices/refresh");
        AllowAnonymous();
        Description(b => b.WithTags("Prices").Produces<PriceRefreshState>(StatusCodes.Status202Accepted));
        Summary(s =>
        {
            s.Summary = "Fetch prices for every holding now, in the background.";
            s.Description = "Queues a refresh of every price series and returns 202 at once; poll GET /api/prices/status for progress.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        _queue.Enqueue(PriceRefreshRequest.Everything(PriceRefreshTrigger.Manual));
        await Send.ResponseAsync(_status.Current, StatusCodes.Status202Accepted, ct);
    }
}
