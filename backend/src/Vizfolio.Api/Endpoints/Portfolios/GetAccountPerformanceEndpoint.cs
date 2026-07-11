using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetAccountPerformanceRequest(
    Guid PortfolioId,
    Guid AccountId,
    DateOnly? From,
    DateOnly? To);

public sealed class GetAccountPerformanceEndpoint
    : Endpoint<GetAccountPerformanceRequest, PortfolioPerformanceResponse>
{
    private readonly IPortfolioPerformanceService _performance;

    public GetAccountPerformanceEndpoint(IPortfolioPerformanceService performance)
    {
        _performance = performance;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/performance");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<PortfolioPerformanceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "Get performance (starting & ending balance) for a single account.";
            s.Params["from"] = "Start of the period (inclusive). Defaults to the earliest transaction in the account.";
            s.Params["to"] = "End of the period (inclusive). Defaults to today (UTC).";
        });
    }

    public override async Task HandleAsync(GetAccountPerformanceRequest req, CancellationToken ct)
    {
        if (req.From.HasValue && req.To.HasValue && req.From.Value > req.To.Value)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var result = await _performance.ComputeForAccountAsync(
            req.PortfolioId, req.AccountId, req.From, req.To, ct);
        if (result is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(PerformanceMapping.ToResponse(result), ct);
    }
}
