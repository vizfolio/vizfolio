using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetAccountHoldingsRequest(Guid PortfolioId, Guid AccountId, DateOnly? AsOf);

/// <summary>
/// Lists an account's holdings valued on <c>asOf</c> by its <see cref="AccountStateEngine"/> — exactly the rules the
/// performance balances use, so the Holdings and Performance views always agree. The settlement fund (and any
/// cash holding) is the account's cash, so it's shown as a single <c>Cash</c> row valued from the cash roll.
/// </summary>
public sealed class GetAccountHoldingsEndpoint
    : Endpoint<GetAccountHoldingsRequest, IReadOnlyList<HoldingResponse>>
{
    private readonly IAppDbContext _db;
    private readonly AccountValuationLoader _valuationLoader;
    private readonly IPriceRefreshStatus _priceRefresh;

    public GetAccountHoldingsEndpoint(IAppDbContext db, AccountValuationLoader valuationLoader, IPriceRefreshStatus priceRefresh)
    {
        _db = db;
        _valuationLoader = valuationLoader;
        _priceRefresh = priceRefresh;
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
            s.Summary = "List an account's holdings and cash valued on a date (price history, money-market $1.00, or snapshot).";
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
            .Select(h => new { h.AccountHoldingId, h.Kind, h.Symbol, h.Name, h.Cusip, h.Isin, h.CurrencyCode })
            .ToListAsync(ct);

        var valuation = await _valuationLoader.LoadAsync([req.AccountId], asOf, ct);
        var engine = valuation.Engines[req.AccountId];
        var value = engine.ValueAt(asOf);
        var byHolding = value.Components.Where(c => !c.IsCash).ToDictionary(c => c.HoldingId!.Value);

        // Latest snapshot per holding on/before asOf (AsOf is unique per holding).
        var latestByHolding = valuation.Snapshots
            .Where(s => s.AsOf <= asOf)
            .GroupBy(s => s.AccountHoldingId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.AsOf)!);

        var pricesPending = _priceRefresh.IsPending(req.AccountId);
        string? CauseOf(ComponentValue v)
            => v.Status == HoldingValuationStatus.Missing ? MissingCauseNames.Of(v.Cause, pricesPending) : null;

        var results = new List<HoldingResponse>();
        var cashHoldings = new List<Guid>();
        foreach (var h in holdings)
        {
            if (!byHolding.TryGetValue(h.AccountHoldingId, out var v))
            {
                cashHoldings.Add(h.AccountHoldingId); // settlement fund or cash: reported in the Cash row
                continue;
            }

            latestByHolding.TryGetValue(h.AccountHoldingId, out var snap);
            var marketValue = MarketValue(v);
            // A snapshot's cost basis only describes the position it recorded.
            var costBasis = snap is not null && v.Quantity == snap.Quantity ? snap.CostBasis : null;
            var gainLoss = marketValue is { } mv && costBasis is { } cb ? mv - cb : (decimal?)null;

            results.Add(new HoldingResponse(
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
                v.Status == HoldingValuationStatus.NotHeld ? 0m : v.Quantity,
                v.UnitPrice,
                marketValue,
                costBasis,
                gainLoss,
                StatusName(v.Status),
                v.Source?.ToString(),
                v.PriceAsOf ?? v.SnapshotAsOf,
                CauseOf(v)));
        }

        results = results
            .OrderBy(h => h.Symbol ?? h.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One Cash row (last) whenever the account has a settlement fund or cash holding, or holds cash.
        var cash = value.Components.Single(c => c.IsCash);
        if (cashHoldings.Count > 0 || cash.Status != HoldingValuationStatus.NotHeld)
        {
            var settlement = holdings
                .Where(h => cashHoldings.Contains(h.AccountHoldingId) && h.Kind != AccountHoldingKind.Cash)
                .Select(h => h.Symbol)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToList();
            var cashHolding = holdings.FirstOrDefault(h => h.Kind == AccountHoldingKind.Cash);
            var cashSnapshot = cashHoldings
                .Select(id => latestByHolding.GetValueOrDefault(id))
                .OfType<HoldingSnapshotRow>()
                .MaxBy(s => s.AsOf);
            results.Add(new HoldingResponse(
                cashHolding?.AccountHoldingId ?? cashHoldings.FirstOrDefault(),
                nameof(AccountHoldingKind.Cash),
                "Cash",
                settlement.Count > 0 ? $"Cash & settlement fund ({string.Join(", ", settlement)})" : "Cash",
                null,
                null,
                cashHolding?.CurrencyCode ?? valuation.ReportingCurrency,
                cashSnapshot is not null,
                cashSnapshot?.AsOf,
                cashSnapshot?.Source.ToString(),
                cash.Status == HoldingValuationStatus.NotHeld ? 0m : cash.Quantity,
                1m,
                MarketValue(cash),
                null,
                null,
                StatusName(cash.Status),
                cash.Source?.ToString(),
                null,
                CauseOf(cash)));
        }

        await Send.OkAsync(results, ct);
    }

    private static decimal? MarketValue(ComponentValue v) => v.Status switch
    {
        HoldingValuationStatus.Covered => v.Value,
        HoldingValuationStatus.NotHeld => 0m,
        _ => null,
    };

    private static string StatusName(HoldingValuationStatus status) => status switch
    {
        HoldingValuationStatus.Covered => "Valued",
        HoldingValuationStatus.NotHeld => "NotHeld",
        _ => "Missing",
    };
}
