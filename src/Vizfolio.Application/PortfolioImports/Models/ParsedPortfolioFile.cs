namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedPortfolioFile(
    string SourceSystem,
    string? SourceInstitution,
    string? SourceAccountNumber,
    IReadOnlyList<ParsedTransaction> Transactions);
