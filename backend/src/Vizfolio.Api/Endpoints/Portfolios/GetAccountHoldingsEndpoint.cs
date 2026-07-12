using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetAccountHoldingsRequest(Guid PortfolioId, Guid AccountId, DateOnly? AsOf);

/// <summary>
/// Lists an account's holdings, each valued from the latest <c>AccountHoldingSnapshot</c> with
/// <c>AsOf &lt;= asOf</c> — the same snapshot rule the performance balances are built from.
/// </summary>
public sealed class GetAccountHoldingsEndpoint
    : Endpoint<GetAccountHoldingsRequest, IReadOnlyList<HoldingResponse>>
{
    private readonly IAppDbContext _db;

    public GetAccountHoldingsEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/holdings");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<IReadOnlyList<HoldingResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "List an account's holdings with their latest valuation.";
            s.Params["asOf"] = "Valuation date (inclusive). Defaults to today (UTC).";
        });
    }

    public override async Task HandleAsync(GetAccountHoldingsRequest req, CancellationToken ct)
    {
        var accountInScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == req.PortfolioId && a.AccountId == req.AccountId, ct);
        if (!accountInScope)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var asOf = req.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var holdings = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => h.AccountId == req.AccountId)
            .Select(h => new
            {
                h.AccountHoldingId,
                h.Kind,
                h.Symbol,
                h.Name,
                h.Cusip,
                h.Isin,
                h.CurrencyCode,
            })
            .ToListAsync(ct);

        var holdingIds = holdings.Select(h => h.AccountHoldingId).ToList();

        var snapshots = await _db.AccountHoldingSnapshots
            .AsNoTracking()
            .Where(s => holdingIds.Contains(s.AccountHoldingId) && s.AsOf <= asOf)
            .ToListAsync(ct);

        // Latest snapshot per holding: newest AsOf, breaking ties by most recently recorded.
        var latestByHolding = snapshots
            .GroupBy(s => s.AccountHoldingId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.AsOf).ThenByDescending(s => s.RecordedAt).First());

        var results = holdings
            .Select(h =>
            {
                latestByHolding.TryGetValue(h.AccountHoldingId, out var snap);
                var gainLoss = snap is { MarketValue: { } mv, CostBasis: { } cb } ? mv - cb : (decimal?)null;
                return new HoldingResponse(
                    h.AccountHoldingId,
                    h.Kind.ToString(),
                    h.Symbol,
                    h.Name,
                    h.Cusip,
                    h.Isin,
                    h.CurrencyCode,
                    snap is not null,
                    snap?.AsOf,
                    snap?.Source.ToString(),
                    snap?.Quantity,
                    snap?.UnitPrice,
                    snap?.MarketValue,
                    snap?.CostBasis,
                    gainLoss);
            })
            .OrderBy(h => h.Symbol ?? h.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await Send.OkAsync(results, ct);
    }
}
