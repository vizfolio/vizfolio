using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios.Valuation;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetOpeningPositionsRequest(Guid PortfolioId, Guid AccountId);

/// <summary>
/// The account's starting positions — what it held the day before its first transaction — as the valuation engine
/// derives them from the broker's statements. <c>Class</c> is <c>None</c> (nothing held), <c>PreHistory</c> (held
/// before the imported history) or <c>Inconsistent</c> (the ledger and the broker disagree, so it can't be derived);
/// <c>Verified</c> is false when no statement exists to derive it from (assumed zero). Values are at <c>AsOf</c>.
/// </summary>
public sealed record OpeningPositionsResponse(
    DateOnly? AsOf,
    IReadOnlyList<OpeningPositionResponse> Holdings,
    OpeningPositionResponse Cash);

public sealed record OpeningPositionResponse(
    Guid? AccountHoldingId,
    string? Symbol,
    decimal Quantity,
    string Class,
    bool Verified,
    decimal? UnitPrice,
    decimal? MarketValue);

public sealed class GetOpeningPositionsEndpoint
    : Endpoint<GetOpeningPositionsRequest, OpeningPositionsResponse>
{
    private readonly IAppDbContext _db;
    private readonly AccountValuationLoader _valuationLoader;

    public GetOpeningPositionsEndpoint(IAppDbContext db, AccountValuationLoader valuationLoader)
    {
        _db = db;
        _valuationLoader = valuationLoader;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/opening-positions");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<OpeningPositionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary =
            "The account's starting positions (the day before its first transaction), derived from its statements.");
    }

    public override async Task HandleAsync(GetOpeningPositionsRequest req, CancellationToken ct)
    {
        var accountInScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == req.PortfolioId && a.AccountId == req.AccountId, ct);
        if (!accountInScope)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var loaded = await _valuationLoader.LoadAsync([req.AccountId], DateOnly.MaxValue, ct);
        var engine = loaded.Engines[req.AccountId];
        var components = engine.OpeningDate is { } opening
            ? engine.ValueAt(opening).Components.ToDictionary(c => c.HoldingId ?? Guid.Empty)
            : new Dictionary<Guid, ComponentValue>();

        OpeningPositionResponse ToResponse(DerivedOpening o)
        {
            components.TryGetValue(o.HoldingId ?? Guid.Empty, out var value);
            var valued = value?.Status == HoldingValuationStatus.Covered;
            return new OpeningPositionResponse(
                o.HoldingId,
                o.Symbol,
                o.Quantity,
                o.Class.ToString(),
                o.Verified,
                valued ? value!.UnitPrice : null,
                valued ? value!.Value : o.Class == OpeningClass.None ? 0m : null);
        }

        var openings = engine.Openings;
        await Send.OkAsync(new OpeningPositionsResponse(
            engine.OpeningDate,
            openings.Where(o => o.HoldingId is not null)
                .OrderBy(o => o.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(ToResponse)
                .ToList(),
            ToResponse(openings.Single(o => o.HoldingId is null))), ct);
    }
}
