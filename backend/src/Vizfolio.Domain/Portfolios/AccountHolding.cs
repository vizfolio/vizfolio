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

    /// <summary>
    /// The account's settlement (core / sweep) fund, as its broker marks it: its shares are the account's cash.
    /// Set by imports from broker knowledge (see docs/performance-api.md → "Broker profiles").
    /// </summary>
    public bool IsSettlementFund { get; private set; }

    /// <summary>The upload that created this holding; undoing it may remove the holding if nothing else uses it.</summary>
    public Guid? CreatedByImportBatchId { get; private set; }

    public void MarkSettlementFund()
    {
        IsSettlementFund = true;
    }

    public void MarkCreatedByImport(Guid importBatchId)
    {
        if (importBatchId == Guid.Empty)
            throw new ArgumentException("Import batch ID is required.", nameof(importBatchId));

        CreatedByImportBatchId ??= importBatchId;
    }

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

    /// <summary>
    /// Promotes a holding created before its reference data was known (<see cref="AccountHoldingKind.Other"/>,
    /// e.g. an unrecognised ticker or an opening balance) to a <see cref="AccountHoldingKind.Security"/>. The
    /// holding keeps its id, so its transactions and snapshots stay attached.
    /// </summary>
    public void PromoteToSecurity(Guid securityId)
    {
        EnsurePromotable(securityId);
        Kind = AccountHoldingKind.Security;
        LinkToSecurity(securityId);
    }

    /// <summary>As <see cref="PromoteToSecurity"/>, for a holding now recognised as a fund share class.</summary>
    public void PromoteToFund(Guid fundId)
    {
        EnsurePromotable(fundId);
        Kind = AccountHoldingKind.Fund;
        LinkToFund(fundId);
    }

    private void EnsurePromotable(Guid referenceId)
    {
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference ID is required.", nameof(referenceId));
        if (Kind != AccountHoldingKind.Other)
            throw new InvalidOperationException($"Only an unclassified (Other) holding can be promoted; this one is {Kind}.");
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
