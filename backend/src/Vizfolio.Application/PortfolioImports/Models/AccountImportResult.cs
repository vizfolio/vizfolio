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
    /// Rows already stored from this source that the parser now maps differently (type, amount, quantity, price,
    /// settlement date, label or settlement-fund flag — e.g. after a mapping fix), updated in place. They are also
    /// counted in <see cref="Skipped"/>.
    /// </summary>
    public int Updated { get; init; }

    /// <summary>Statement positions (and cash balances) recorded as snapshots.</summary>
    public int SnapshotsInserted { get; init; }

    /// <summary>
    /// Contributions the account's history implies but never recorded (purchases with no deposit), stored as
    /// labelled ledger rows after this import. Totals for the whole account, not just this file.
    /// </summary>
    public int ImpliedContributions { get; init; }

    /// <summary>Sum of <see cref="ImpliedContributions"/>.</summary>
    public decimal ImpliedContributionsAmount { get; init; }
}
