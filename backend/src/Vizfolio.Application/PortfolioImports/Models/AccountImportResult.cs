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
    IReadOnlyList<PortfolioImportFailure> Failures)
{
    /// <summary>
    /// Contributions the account's history implies but never recorded (purchases with no deposit), stored as
    /// labelled ledger rows after this import. Totals for the whole account, not just this file.
    /// </summary>
    public int ImpliedContributions { get; init; }

    /// <summary>Sum of <see cref="ImpliedContributions"/>.</summary>
    public decimal ImpliedContributionsAmount { get; init; }
}
