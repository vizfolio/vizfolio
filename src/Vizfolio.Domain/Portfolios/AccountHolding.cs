namespace Vizfolio.Domain.Portfolios;

public sealed class AccountHolding
{
    private AccountHolding() { }

    public AccountHolding(Guid accountId, AccountHoldingKind kind)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        AccountHoldingId = Guid.NewGuid();
        AccountId = accountId;
        Kind = kind;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid AccountHoldingId { get; private set; }

    public Guid AccountId { get; private set; }

    public AccountHoldingKind Kind { get; private set; }

    public string? Symbol { get; private set; }

    public string? Name { get; private set; }

    public string? Isin { get; private set; }

    public string? Cusip { get; private set; }

    public string? AssetCategoryCode { get; private set; }

    public string? AssetClassCode { get; private set; }

    public string? CurrencyCode { get; private set; }

    public Guid? SecurityId { get; private set; }

    public Guid? FundId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetIdentifiers(string? symbol, string? name, string? isin, string? cusip)
    {
        Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Isin = string.IsNullOrWhiteSpace(isin) ? null : isin.Trim().ToUpperInvariant();
        Cusip = string.IsNullOrWhiteSpace(cusip) ? null : cusip.Trim().ToUpperInvariant();
    }

    public void SetClassification(string? assetCategoryCode, string? assetClassCode)
    {
        AssetCategoryCode = string.IsNullOrWhiteSpace(assetCategoryCode)
            ? null
            : assetCategoryCode.Trim().ToUpperInvariant();
        AssetClassCode = string.IsNullOrWhiteSpace(assetClassCode)
            ? null
            : assetClassCode.Trim().ToUpperInvariant();
    }

    public void SetCurrency(string? currencyCode)
    {
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode)
            ? null
            : currencyCode.Trim().ToUpperInvariant();
    }

    public void LinkToSecurity(Guid securityId)
    {
        if (Kind != AccountHoldingKind.Security)
            throw new InvalidOperationException(
                $"Cannot link to a Security when holding kind is {Kind}.");
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));

        SecurityId = securityId;
    }

    public void LinkToFund(Guid fundId)
    {
        if (Kind != AccountHoldingKind.Fund)
            throw new InvalidOperationException(
                $"Cannot link to a Fund when holding kind is {Kind}.");
        if (fundId == Guid.Empty)
            throw new ArgumentException("Fund ID is required.", nameof(fundId));

        FundId = fundId;
    }
}
