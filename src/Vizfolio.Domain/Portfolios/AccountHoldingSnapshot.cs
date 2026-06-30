namespace Vizfolio.Domain.Portfolios;

public sealed class AccountHoldingSnapshot
{
    private AccountHoldingSnapshot() { }

    public AccountHoldingSnapshot(
        Guid accountHoldingId,
        DateOnly asOf,
        decimal quantity,
        AccountHoldingSnapshotSource source)
    {
        if (accountHoldingId == Guid.Empty)
            throw new ArgumentException("Account holding ID is required.", nameof(accountHoldingId));

        AccountHoldingSnapshotId = Guid.NewGuid();
        AccountHoldingId = accountHoldingId;
        AsOf = asOf;
        Quantity = quantity;
        Source = source;
        RecordedAt = DateTimeOffset.UtcNow;
    }

    public Guid AccountHoldingSnapshotId { get; private set; }

    public Guid AccountHoldingId { get; private set; }

    public DateOnly AsOf { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal? CostBasis { get; private set; }

    public decimal? MarketValue { get; private set; }

    public decimal? UnitPrice { get; private set; }

    public string? CurrencyCode { get; private set; }

    public AccountHoldingSnapshotSource Source { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public void SetValuation(
        decimal? costBasis,
        decimal? marketValue,
        decimal? unitPrice,
        string? currencyCode)
    {
        CostBasis = costBasis;
        MarketValue = marketValue;
        UnitPrice = unitPrice;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode)
            ? null
            : currencyCode.Trim().ToUpperInvariant();
    }
}
