using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Portfolios.Health;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetPortfolioHealthRequest(Guid PortfolioId);

public sealed class GetPortfolioHealthEndpoint : Endpoint<GetPortfolioHealthRequest, DataHealthReport>
{
    private readonly IDataHealthService _health;

    public GetPortfolioHealthEndpoint(IDataHealthService health)
    {
        _health = health;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/health");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<DataHealthReport>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "Everything that makes the portfolio's numbers blank, approximate or assumed, with what to do about it.";
            s.Description =
                "Findings per account: statements the ledger disagrees with, positions it can't explain, days a holding " +
                "has no usable price, unpriced transfers, rows the parsers didn't understand and implied contributions. " +
                "`Blocking` findings make a headline number blank or approximate; `Info` ones are worth knowing.";
        });
    }

    public override async Task HandleAsync(GetPortfolioHealthRequest req, CancellationToken ct)
    {
        var report = await _health.GetForPortfolioAsync(req.PortfolioId, ct);
        if (report is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(report, ct);
    }
}

public sealed record GetAccountHealthRequest(Guid PortfolioId, Guid AccountId);

public sealed class GetAccountHealthEndpoint : Endpoint<GetAccountHealthRequest, DataHealthReport>
{
    private readonly IDataHealthService _health;

    public GetAccountHealthEndpoint(IDataHealthService health)
    {
        _health = health;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/health");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<DataHealthReport>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary = "One account's data health findings (see GET /portfolios/{portfolioId}/health).");
    }

    public override async Task HandleAsync(GetAccountHealthRequest req, CancellationToken ct)
    {
        var report = await _health.GetForAccountAsync(req.PortfolioId, req.AccountId, ct);
        if (report is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(report, ct);
    }
}
