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
            .Produces(StatusCodes.Status400BadRequest)
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

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.InstitutionCode)
            || Account.NormalizeAccountNumber(req.AccountNumber).Length == 0)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        // Matched the way imports match accounts: "1234-5678" and "12345678" are the same account.
        var institutionCode = req.InstitutionCode.Trim().ToLowerInvariant();
        var accountNumber = req.AccountNumber.Trim();
        var normalized = Account.NormalizeAccountNumber(accountNumber);
        var numbers = await _db.Accounts.AsNoTracking()
            .Where(a => a.PortfolioId == req.PortfolioId && a.InstitutionCode == institutionCode)
            .Select(a => a.AccountNumber)
            .ToListAsync(ct);
        if (numbers.Any(n => Account.NormalizeAccountNumber(n) == normalized))
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
