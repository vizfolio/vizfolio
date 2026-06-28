namespace Vizfolio.Domain.Portfolios;

public sealed class AccountTransaction
{
    private AccountTransaction() { }

    public AccountTransaction(
        Guid accountId,
        string sourceSystem,
        string externalId,
        TransactionType type,
        DateOnly tradeDate,
        decimal amount)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(sourceSystem))
            throw new ArgumentException("Source system is required.", nameof(sourceSystem));
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("External ID is required.", nameof(externalId));

        AccountTransactionId = Guid.NewGuid();
        AccountId = accountId;
        SourceSystem = sourceSystem.Trim().ToUpperInvariant();
        ExternalId = externalId.Trim();
        Type = type;
        TradeDate = tradeDate;
        Amount = amount;
        ImportedAt = DateTimeOffset.UtcNow;
    }

    public Guid AccountTransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    public string SourceSystem { get; private set; } = string.Empty;

    public string ExternalId { get; private set; } = string.Empty;

    public TransactionType Type { get; private set; }

    public DateOnly TradeDate { get; private set; }

    public DateOnly? SettlementDate { get; private set; }

    public string? Ticker { get; private set; }

    public string? Cusip { get; private set; }

    public Guid? SecurityId { get; private set; }

    public decimal? Quantity { get; private set; }

    public decimal? Price { get; private set; }

    public decimal Amount { get; private set; }

    public decimal? Fees { get; private set; }

    public string? CurrencyCode { get; private set; }

    public string? Memo { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public void SetSecurityReference(string? ticker, string? cusip)
    {
        Ticker = string.IsNullOrWhiteSpace(ticker) ? null : ticker.Trim().ToUpperInvariant();
        Cusip = string.IsNullOrWhiteSpace(cusip) ? null : cusip.Trim().ToUpperInvariant();
    }

    public void LinkToSecurity(Guid securityId)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));

        SecurityId = securityId;
    }

    public void SetTradeDetails(decimal? quantity, decimal? price, decimal? fees, DateOnly? settlementDate)
    {
        Quantity = quantity;
        Price = price;
        Fees = fees;
        SettlementDate = settlementDate;
    }

    public void SetCurrency(string? currencyCode)
    {
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode)
            ? null
            : currencyCode.Trim().ToUpperInvariant();
    }

    public void SetMemo(string? memo)
    {
        Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
    }
}
