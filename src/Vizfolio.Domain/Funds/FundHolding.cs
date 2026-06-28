namespace Vizfolio.Domain.Funds;

public sealed class FundHolding
{
    private FundHolding() { }

    public FundHolding(Guid fundSnapshotId, decimal weight)
    {
        if (fundSnapshotId == Guid.Empty)
            throw new ArgumentException("Fund snapshot ID is required.", nameof(fundSnapshotId));

        FundHoldingId = Guid.NewGuid();
        FundSnapshotId = fundSnapshotId;
        Weight = weight;
    }

    public Guid FundHoldingId { get; private set; }

    public Guid FundSnapshotId { get; private set; }

    public Guid? SecurityId { get; private set; }

    public decimal Weight { get; private set; }

    public decimal? FairValueUsd { get; private set; }

    public decimal? Balance { get; private set; }

    public string? Units { get; private set; }

    public string? Name { get; private set; }

    public string? Ticker { get; private set; }

    public string? Isin { get; private set; }

    public string? AssetCategory { get; private set; }

    public string? Country { get; private set; }

    public string? Currency { get; private set; }

    public void LinkToSecurity(Guid securityId)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));

        SecurityId = securityId;
    }

    public void SetIdentifiers(string? name, string? ticker, string? isin)
    {
        Name = name;
        Ticker = string.IsNullOrWhiteSpace(ticker) ? null : ticker.Trim().ToUpperInvariant();
        Isin = string.IsNullOrWhiteSpace(isin) ? null : isin.Trim().ToUpperInvariant();
    }

    public void SetValuation(decimal? fairValueUsd, decimal? balance, string? units)
    {
        FairValueUsd = fairValueUsd;
        Balance = balance;
        Units = string.IsNullOrWhiteSpace(units) ? null : units.Trim();
    }

    public void SetClassification(string? assetCategory, string? country, string? currency)
    {
        AssetCategory = assetCategory;
        Country = country;
        Currency = currency;
    }
}
