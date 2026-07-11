using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record CreatePortfolioRequest(string Name);

public sealed class CreatePortfolioEndpoint : Endpoint<CreatePortfolioRequest, PortfolioResponse>
{
    private readonly IAppDbContext _db;

    public CreatePortfolioEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Post("/portfolios");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<PortfolioResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest));
        Summary(s =>
        {
            s.Summary = "Create a new portfolio.";
            s.Description = "Portfolios are grouping containers for one or more brokerage accounts.";
            s.ExampleRequest = new CreatePortfolioRequest("Retirement");
        });
    }

    public override async Task HandleAsync(CreatePortfolioRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Name))
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var portfolio = new Portfolio(req.Name);
        _db.Portfolios.Add(portfolio);
        await _db.SaveChangesAsync(ct);

        var response = new PortfolioResponse(
            portfolio.PortfolioId, portfolio.Name, portfolio.CreatedAt, 0);
        await Send.CreatedAtAsync<GetPortfolioEndpoint>(
            new { portfolioId = portfolio.PortfolioId }, response, cancellation: ct);
    }
}
