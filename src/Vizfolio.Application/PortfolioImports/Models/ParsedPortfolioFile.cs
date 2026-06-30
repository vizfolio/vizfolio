namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedPortfolioFile(
    string SourceSystem,
    IReadOnlyList<ParsedAccountStatement> Statements);
