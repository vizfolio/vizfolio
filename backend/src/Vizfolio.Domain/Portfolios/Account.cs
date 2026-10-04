namespace Vizfolio.Domain.Portfolios;

public sealed class Account
{
    private readonly List<AccountTransaction> _transactions = [];
    private readonly List<AccountHolding> _holdings = [];

    private Account() { }

    public Account(Guid portfolioId, string name, string institutionCode, string accountNumber, string? accountType = null)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required.", nameof(portfolioId));

        AccountId = Guid.NewGuid();
        PortfolioId = portfolioId;
        Rename(name);
        SetInstitutionCode(institutionCode);
        SetAccountNumber(accountNumber);
        SetAccountType(accountType);
    }

    public static Account FromImport(Guid portfolioId, string institutionCode, string accountNumber)
    {
        var normalizedCode = (institutionCode ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedNumber = (accountNumber ?? string.Empty).Trim();
        var name = $"{normalizedCode} {normalizedNumber}".Trim();
        return new Account(portfolioId, name, normalizedCode, normalizedNumber);
    }

    public Guid AccountId { get; private set; }

    public Guid PortfolioId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string InstitutionCode { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public string? AccountType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The upload that created this account; undoing it removes the account if nothing else uses it.</summary>
    public Guid? CreatedByImportBatchId { get; private set; }

    /// <summary>
    /// <see cref="AccountNumber"/> reduced to its letters and digits, upper-cased, so "1234-5678", "12345678 " and a
    /// hand-typed "1234 5678" all match the same statement.
    /// </summary>
    public static string NormalizeAccountNumber(string? accountNumber)
        => new((accountNumber ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public void MarkCreatedByImport(Guid importBatchId)
    {
        if (importBatchId == Guid.Empty)
            throw new ArgumentException("Import batch ID is required.", nameof(importBatchId));

        CreatedByImportBatchId ??= importBatchId;
    }

    public IReadOnlyList<AccountTransaction> Transactions => _transactions;

    public IReadOnlyList<AccountHolding> Holdings => _holdings;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Account name cannot be empty.", nameof(name));

        Name = name.Trim();
    }

    /// <summary>
    /// Corrects the account number — when an import shows the one typed in was wrong and the user confirms the
    /// file's number is the account's.
    /// </summary>
    public void ChangeAccountNumber(string accountNumber) => SetAccountNumber(accountNumber);

    public void SetAccountType(string? accountType)
    {
        AccountType = string.IsNullOrWhiteSpace(accountType) ? null : accountType.Trim();
    }

    private void SetInstitutionCode(string institutionCode)
    {
        if (string.IsNullOrWhiteSpace(institutionCode))
            throw new ArgumentException("Institution code is required.", nameof(institutionCode));

        InstitutionCode = institutionCode.Trim().ToLowerInvariant();
    }

    private void SetAccountNumber(string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required.", nameof(accountNumber));

        AccountNumber = accountNumber.Trim();
    }
}
