namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record PortfolioResponse(Guid PortfolioId, string Name, DateTimeOffset CreatedAt, int AccountCount);

public sealed record AccountResponse(
    Guid AccountId,
    Guid PortfolioId,
    string Name,
    string Institution,
    string AccountNumber,
    string? AccountType,
    DateTimeOffset CreatedAt,
    int TransactionCount);
