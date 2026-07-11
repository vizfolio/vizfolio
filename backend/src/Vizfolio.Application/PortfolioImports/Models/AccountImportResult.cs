namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record AccountImportResult(
    Guid AccountId,
    bool Created,
    string? InstitutionCode,
    string? AccountNumber,
    int Considered,
    int Inserted,
    int Skipped,
    int Failed,
    IReadOnlyList<PortfolioImportFailure> Failures);
