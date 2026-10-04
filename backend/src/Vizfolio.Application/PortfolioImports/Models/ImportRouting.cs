namespace Vizfolio.Application.PortfolioImports.Models;

/// <summary>
/// What the user decided after an import asked (<see cref="PortfolioImportStatus.NeedsAccountSelection"/> or
/// <see cref="PortfolioImportStatus.LikelyOtherAccount"/>); sent with the same file again.
/// </summary>
public sealed record ImportChoices
{
    public static readonly ImportChoices None = new();

    /// <summary>Where each of the file's statements goes, keyed by <see cref="StatementAssignment.FileAccountNumber"/>.</summary>
    public IReadOnlyList<StatementAssignment> Assignments { get; init; } = [];

    /// <summary>Import into the account it was uploaded to even though the file looks like another account's.</summary>
    public bool IgnoreRoutingCheck { get; init; }

    internal StatementAssignment? For(string fileAccountNumber) =>
        Assignments.FirstOrDefault(a => Domain.Portfolios.Account.NormalizeAccountNumber(a.FileAccountNumber) == fileAccountNumber);
}

/// <summary>
/// One statement's destination: an existing account (<see cref="AccountId"/>), or a new one (<see cref="NewAccount"/>).
/// <see cref="FileAccountNumber"/> is the account number the file gives (normalized), or empty when it gives none.
/// </summary>
public sealed record StatementAssignment(string FileAccountNumber, Guid? AccountId, NewAccountDetails? NewAccount);

/// <summary>
/// A new account for a statement. A statement without an account number needs the institution and number from the
/// user (every account has one); a statement that has them uses the file's, and only <see cref="Name"/> applies.
/// </summary>
public sealed record NewAccountDetails(string? Name, string? InstitutionCode, string? AccountNumber);

/// <summary>A statement the import couldn't place on its own, and the accounts it could belong to.</summary>
public sealed record AccountSelection(
    // The account number the file gives (normalized), or "" when it gives none; echo it in the StatementAssignment.
    string FileAccountNumber,
    string? InstitutionCode,
    // Why it asks: NoAccountNumber, UnknownAccountNumber (a new number that looks like an existing account), or
    // LikelyOtherAccount (uploaded to one account, looks like another's).
    string Reason,
    int Rows,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    IReadOnlyList<RoutingCandidate> Candidates,
    // The account the evidence points to, if any — pre-select it.
    Guid? SuggestedAccountId);

public static class AccountSelectionReasons
{
    public const string NoAccountNumber = nameof(NoAccountNumber);
    public const string UnknownAccountNumber = nameof(UnknownAccountNumber);
    public const string LikelyOtherAccount = nameof(LikelyOtherAccount);
}

/// <summary>
/// How strongly a file's rows point at one account: how many it already holds (<see cref="MatchingRows"/>) out of the
/// file's rows dated within its history (<see cref="RowsInAccountRange"/>), and how many tickers they share.
/// </summary>
public sealed record RoutingCandidate(
    Guid AccountId,
    string Name,
    string InstitutionCode,
    string AccountNumberMasked,
    int MatchingRows,
    int RowsInAccountRange,
    int SharedTickers);

/// <summary>How an account was chosen for a statement, and the evidence when it was by matching transactions.</summary>
public sealed record AccountRouting(string Method, int? MatchingRows = null);

public static class RoutingMethods
{
    /// <summary>Uploaded to this account.</summary>
    public const string Uploaded = nameof(Uploaded);

    /// <summary>The file's account number matched.</summary>
    public const string AccountNumber = nameof(AccountNumber);

    /// <summary>The file had no account number; the account already held many of its transactions.</summary>
    public const string Fingerprint = nameof(Fingerprint);

    /// <summary>Chosen by the user when the import asked.</summary>
    public const string UserSelected = nameof(UserSelected);

    /// <summary>A new account was created for it.</summary>
    public const string Created = nameof(Created);
}
