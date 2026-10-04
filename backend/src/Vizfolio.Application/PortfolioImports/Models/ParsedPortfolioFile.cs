namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedPortfolioFile(
    string SourceSystem,
    IReadOnlyList<ParsedAccountStatement> Statements)
{
    /// <summary>Everything in the file the parser couldn't fully understand. Nothing is dropped without one.</summary>
    public IReadOnlyList<ImportWarning> Warnings { get; init; } = [];
}
