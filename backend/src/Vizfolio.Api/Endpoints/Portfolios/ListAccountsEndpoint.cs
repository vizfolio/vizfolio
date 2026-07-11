using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record ListAccountsRequest(Guid PortfolioId);

public sealed class ListAccountsEndpoint : Endpoint<ListAccountsRequest, IReadOnlyList<AccountResponse>>
{
    private readonly IAppDbContext _db;

    public ListAccountsEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<IReadOnlyList<AccountResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary = "List accounts inside a portfolio.");
    }

    public override async Task HandleAsync(ListAccountsRequest req, CancellationToken ct)
    {
        var portfolioExists = await _db.Portfolios.AsNoTracking()
            .AnyAsync(p => p.PortfolioId == req.PortfolioId, ct);
        if (!portfolioExists)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var results = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.PortfolioId == req.PortfolioId)
            .OrderBy(a => a.Name)
            .Select(a => new AccountResponse(
                a.AccountId,
                a.PortfolioId,
                a.Name,
                a.InstitutionCode,
                a.AccountNumber,
                a.AccountType,
                a.CreatedAt,
                _db.AccountTransactions.Count(t => t.AccountId == a.AccountId)))
            .ToListAsync(ct);

        await Send.OkAsync(results, ct);
    }
}
