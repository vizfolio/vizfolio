using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record CreateAccountRequest(
    Guid PortfolioId,
    string Name,
    string InstitutionCode,
    string AccountNumber,
    string? AccountType);

public sealed class CreateAccountEndpoint : Endpoint<CreateAccountRequest, AccountResponse>
{
    private readonly IAppDbContext _db;

    public CreateAccountEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Post("/portfolios/{portfolioId}/accounts");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<AccountResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Add a brokerage account to a portfolio.";
            s.Description = "Accounts are uniquely identified within a portfolio by (institutionCode, accountNumber). " +
                             "InstitutionCode mirrors OFX `BROKERID` / `BANKID` (e.g. `vanguard.com`) and is normalized to lower-case. " +
                             "AccountType is free-form (e.g. 'Brokerage', 'Roth IRA').";
            s.ExampleRequest = new CreateAccountRequest(
                Guid.Empty, "Fidelity Brokerage", "fidelity.com", "1234", "Brokerage");
        });
    }

    public override async Task HandleAsync(CreateAccountRequest req, CancellationToken ct)
    {
        var portfolioExists = await _db.Portfolios.AsNoTracking()
            .AnyAsync(p => p.PortfolioId == req.PortfolioId, ct);
        if (!portfolioExists)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var institutionCode = (req.InstitutionCode ?? string.Empty).Trim().ToLowerInvariant();
        var accountNumber = req.AccountNumber?.Trim() ?? string.Empty;
        var duplicate = await _db.Accounts.AsNoTracking()
            .AnyAsync(a => a.PortfolioId == req.PortfolioId
                && a.InstitutionCode == institutionCode
                && a.AccountNumber == accountNumber, ct);
        if (duplicate)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status409Conflict, ct);
            return;
        }

        var account = new Account(req.PortfolioId, req.Name, institutionCode, accountNumber, req.AccountType);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        var response = new AccountResponse(
            account.AccountId, account.PortfolioId, account.Name, account.InstitutionCode,
            account.AccountNumber, account.AccountType, account.CreatedAt, 0);

        await Send.CreatedAtAsync<GetAccountEndpoint>(
            new { portfolioId = account.PortfolioId, accountId = account.AccountId }, response, cancellation: ct);
    }
}
