using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetPortfolioRequest(Guid PortfolioId);

public sealed class GetPortfolioEndpoint : Endpoint<GetPortfolioRequest, PortfolioResponse>
{
    private readonly IAppDbContext _db;

    public GetPortfolioEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<PortfolioResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary = "Get a single portfolio by id.");
    }

    public override async Task HandleAsync(GetPortfolioRequest req, CancellationToken ct)
    {
        var portfolio = await _db.Portfolios
            .AsNoTracking()
            .Where(p => p.PortfolioId == req.PortfolioId)
            .Select(p => new PortfolioResponse(
                p.PortfolioId,
                p.Name,
                p.CreatedAt,
                _db.Accounts.Count(a => a.PortfolioId == p.PortfolioId)))
            .SingleOrDefaultAsync(ct);

        if (portfolio is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(portfolio, ct);
    }
}
