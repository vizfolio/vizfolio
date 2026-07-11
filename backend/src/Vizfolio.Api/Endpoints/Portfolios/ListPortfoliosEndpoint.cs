using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed class ListPortfoliosEndpoint : EndpointWithoutRequest<IReadOnlyList<PortfolioResponse>>
{
    private readonly IAppDbContext _db;

    public ListPortfoliosEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/portfolios");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<IReadOnlyList<PortfolioResponse>>(StatusCodes.Status200OK));
        Summary(s => s.Summary = "List all portfolios.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var results = await _db.Portfolios
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PortfolioResponse(
                p.PortfolioId,
                p.Name,
                p.CreatedAt,
                _db.Accounts.Count(a => a.PortfolioId == p.PortfolioId)))
            .ToListAsync(ct);

        await Send.OkAsync(results, ct);
    }
}
