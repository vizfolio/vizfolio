namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedAccountStatement(
    string? InstitutionCode,
    string? AccountNumber,
    IReadOnlyList<ParsedTransaction> Transactions,
    IReadOnlyList<ParsedPosition> Positions,
    DateOnly? AsOf);
