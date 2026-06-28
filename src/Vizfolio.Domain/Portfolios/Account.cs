namespace Vizfolio.Domain.Portfolios;

public sealed class Account
{
    private readonly List<AccountTransaction> _transactions = [];

    private Account() { }

    public Account(Guid portfolioId, string name, string institution, string accountNumber, string? accountType = null)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required.", nameof(portfolioId));

        AccountId = Guid.NewGuid();
        PortfolioId = portfolioId;
        Rename(name);
        SetInstitution(institution);
        SetAccountNumber(accountNumber);
        SetAccountType(accountType);
    }

    public Guid AccountId { get; private set; }

    public Guid PortfolioId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Institution { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public string? AccountType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<AccountTransaction> Transactions => _transactions;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Account name cannot be empty.", nameof(name));

        Name = name.Trim();
    }

    public void SetAccountType(string? accountType)
    {
        AccountType = string.IsNullOrWhiteSpace(accountType) ? null : accountType.Trim();
    }

    private void SetInstitution(string institution)
    {
        if (string.IsNullOrWhiteSpace(institution))
            throw new ArgumentException("Institution is required.", nameof(institution));

        Institution = institution.Trim();
    }

    private void SetAccountNumber(string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required.", nameof(accountNumber));

        AccountNumber = accountNumber.Trim();
    }
}
