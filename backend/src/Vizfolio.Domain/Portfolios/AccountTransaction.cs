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

    public Guid? AccountHoldingId { get; private set; }

    public decimal? Quantity { get; private set; }

    public decimal? Price { get; private set; }

    public decimal Amount { get; private set; }

    public decimal? Fees { get; private set; }

    public string? CurrencyCode { get; private set; }

    public string? Memo { get; private set; }

    /// <summary>
    /// The broker's original, un-normalized transaction type label (e.g. Vanguard's "Sweep in" or
    /// "TRANSFER TO [account]"), preserved verbatim next to the normalized <see cref="Type"/> so no
    /// provenance is lost. Null when the source has no richer type than <see cref="Type"/> already carries.
    /// </summary>
    public string? SourceType { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    /// <summary>The upload that inserted this row (null for rows imported before batches existed, and derived rows).</summary>
    public Guid? ImportBatchId { get; private set; }

    /// <summary>
    /// A split's ratio as the broker reported it (new shares : old shares, e.g. 2 : 1), on <see cref="TransactionType.Split"/>
    /// rows. Null when the broker gave none.
    /// </summary>
    public decimal? SplitNumerator { get; private set; }

    public decimal? SplitDenominator { get; private set; }

    /// <summary>
    /// The broker (or its file format) marks this row as a movement of the account's settlement (core / sweep) fund,
    /// which is the account's cash — see the broker profiles in docs/performance-api.md.
    /// </summary>
    public bool IsSettlementFund { get; private set; }

    /// <summary>
    /// The broker's sub-account for the row (OFX <c>SUBACCTSEC</c>/<c>SUBACCTFUND</c>, e.g. <c>CASH</c> or <c>MARGIN</c>;
    /// <c>FROM→TO</c> for a journal between them). Metadata only: valuation is per account.
    /// </summary>
    public string? SubAccount { get; private set; }

    /// <summary>The split's factor (numerator ÷ denominator), when the broker reported a usable ratio.</summary>
    public decimal? SplitFactor =>
        SplitNumerator is > 0m && SplitDenominator is > 0m ? SplitNumerator / SplitDenominator : null;

    public void SetSecurityReference(string? ticker, string? cusip)
    {
        Ticker = string.IsNullOrWhiteSpace(ticker) ? null : ticker.Trim().ToUpperInvariant();
        Cusip = string.IsNullOrWhiteSpace(cusip) ? null : cusip.Trim().ToUpperInvariant();
    }

    public void LinkToHolding(Guid accountHoldingId)
    {
        if (accountHoldingId == Guid.Empty)
            throw new ArgumentException("Account holding ID is required.", nameof(accountHoldingId));

        AccountHoldingId = accountHoldingId;
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

    /// <summary>
    /// Re-applies the import's normalized fields when a parser now maps the broker's row differently (e.g. after a
    /// mapping fix). Only the import pipeline calls this — never a user edit — so a re-import (or a reprocess of the
    /// stored file) is enough to correct rows already stored.
    /// </summary>
    public void ApplyImportedFields(ImportedTransactionFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Type = fields.Type;
        Amount = fields.Amount;
        Quantity = fields.Quantity;
        Price = fields.Price;
        SettlementDate = fields.SettlementDate;
        SetSourceType(fields.SourceType);
        IsSettlementFund = fields.IsSettlementFund;
    }

    /// <summary>
    /// Replaces a synthetic id (formats without row ids) with its stable form. Only the one-off stable-id migration
    /// calls this; a row's id never changes otherwise.
    /// </summary>
    public void ReassignExternalId(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("External ID is required.", nameof(externalId));

        ExternalId = externalId.Trim();
    }

    public void TagImportBatch(Guid importBatchId)
    {
        if (importBatchId == Guid.Empty)
            throw new ArgumentException("Import batch ID is required.", nameof(importBatchId));

        ImportBatchId = importBatchId;
    }

    public void SetSplitRatio(decimal? numerator, decimal? denominator)
    {
        SplitNumerator = numerator;
        SplitDenominator = denominator;
    }

    public void MarkSettlementFund(bool isSettlementFund = true)
    {
        IsSettlementFund = isSettlementFund;
    }

    public void SetSubAccount(string? subAccount)
    {
        SubAccount = string.IsNullOrWhiteSpace(subAccount) ? null : subAccount.Trim().ToUpperInvariant();
    }

    public void SetSourceType(string? sourceType)
    {
        SourceType = string.IsNullOrWhiteSpace(sourceType) ? null : sourceType.Trim();
    }
}
