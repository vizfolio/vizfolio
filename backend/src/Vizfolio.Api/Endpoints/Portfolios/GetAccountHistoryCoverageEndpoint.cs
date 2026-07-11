using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetAccountHistoryCoverageRequest(Guid PortfolioId, Guid AccountId);

public sealed class GetAccountHistoryCoverageEndpoint
    : Endpoint<GetAccountHistoryCoverageRequest, HistoryCoverageResponse>
{
    private readonly IAccountHistoryService _history;

    public GetAccountHistoryCoverageEndpoint(IAccountHistoryService history)
    {
        _history = history;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/history-coverage");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<HistoryCoverageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary =
            "Report whether the account has a gap between its earliest transaction and its earliest snapshot.");
    }

    public override async Task HandleAsync(GetAccountHistoryCoverageRequest req, CancellationToken ct)
    {
        var result = await _history.GetCoverageAsync(req.PortfolioId, req.AccountId, ct);
        if (result is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(HistoryCoverageMapping.ToResponse(result), ct);
    }
}
