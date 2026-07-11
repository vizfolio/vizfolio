using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed class SetOpeningBalanceEndpoint
    : Endpoint<SetOpeningBalanceRequest, OpeningBalanceResponse>
{
    private readonly IAccountHistoryService _history;

    public SetOpeningBalanceEndpoint(IAccountHistoryService history)
    {
        _history = history;
    }

    public override void Configure()
    {
        Post("/portfolios/{portfolioId}/accounts/{accountId}/opening-balance");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<OpeningBalanceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary =
            "Record a user-supplied opening balance for the account. Creates or replaces OpeningBalance snapshots per holding at AsOf.");
    }

    public override async Task HandleAsync(SetOpeningBalanceRequest req, CancellationToken ct)
    {
        if (req.Holdings is null || req.Holdings.Count == 0)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        foreach (var h in req.Holdings)
        {
            if (string.IsNullOrWhiteSpace(h.Symbol))
            {
                await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
                return;
            }
        }

        var command = new OpeningBalanceCommand(
            req.AsOf,
            req.DefaultCurrencyCode,
            req.Holdings
                .Select(h => new OpeningBalanceHolding(
                    h.Symbol, h.Units, h.MarketValue, h.UnitPrice, h.CostBasis, h.CurrencyCode, h.Cusip))
                .ToList());

        var result = await _history.SetOpeningBalanceAsync(req.PortfolioId, req.AccountId, command, ct);
        if (result is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(HistoryCoverageMapping.ToResponse(result), ct);
    }
}
