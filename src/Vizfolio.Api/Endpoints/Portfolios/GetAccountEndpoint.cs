using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetAccountRequest(Guid PortfolioId, Guid AccountId);

public sealed class GetAccountEndpoint : Endpoint<GetAccountRequest, AccountResponse>
{
    private readonly IAppDbContext _db;

    public GetAccountEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<AccountResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s => s.Summary = "Get an account by id (scoped to its portfolio).");
    }

    public override async Task HandleAsync(GetAccountRequest req, CancellationToken ct)
    {
        var account = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.PortfolioId == req.PortfolioId && a.AccountId == req.AccountId)
            .Select(a => new AccountResponse(
                a.AccountId,
                a.PortfolioId,
                a.Name,
                a.InstitutionCode,
                a.AccountNumber,
                a.AccountType,
                a.CreatedAt,
                _db.AccountTransactions.Count(t => t.AccountId == a.AccountId)))
            .SingleOrDefaultAsync(ct);

        if (account is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(account, ct);
    }
}
