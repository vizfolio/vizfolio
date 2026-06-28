namespace Vizfolio.Domain.Funds;

public sealed class FundSnapshot
{
    private readonly List<ShareClass> _shareClasses = [];
    private readonly List<MonthlyReturn> _monthlyReturns = [];

    private FundSnapshot() { }

    public FundSnapshot(Guid fundId, DateOnly asOf, string sourceFiling, string sourceUrl)
    {
        if (fundId == Guid.Empty)
            throw new ArgumentException("Fund ID is required.", nameof(fundId));
        if (string.IsNullOrWhiteSpace(sourceFiling))
            throw new ArgumentException("Source filing is required.", nameof(sourceFiling));
        if (string.IsNullOrWhiteSpace(sourceUrl))
            throw new ArgumentException("Source URL is required.", nameof(sourceUrl));

        FundSnapshotId = Guid.NewGuid();
        FundId = fundId;
        AsOf = asOf;
        SourceFiling = sourceFiling.Trim();
        SourceUrl = sourceUrl.Trim();
    }

    public Guid FundSnapshotId { get; private set; }

    public Guid FundId { get; private set; }

    public DateOnly AsOf { get; private set; }

    public string SourceFiling { get; private set; } = string.Empty;

    public string SourceUrl { get; private set; } = string.Empty;

    public decimal? NetAssetsUsd { get; private set; }

    public decimal? TotalAssetsUsd { get; private set; }

    public decimal? TotalLiabilitiesUsd { get; private set; }

    public decimal? CashNotInPortfolioUsd { get; private set; }

    public bool IsFinalFiling { get; private set; }

    public IReadOnlyList<ShareClass> ShareClasses => _shareClasses;

    public IReadOnlyList<MonthlyReturn> MonthlyReturns => _monthlyReturns;

    public void SetFinancials(
        decimal? netAssetsUsd,
        decimal? totalAssetsUsd,
        decimal? totalLiabilitiesUsd,
        decimal? cashNotInPortfolioUsd)
    {
        NetAssetsUsd = netAssetsUsd;
        TotalAssetsUsd = totalAssetsUsd;
        TotalLiabilitiesUsd = totalLiabilitiesUsd;
        CashNotInPortfolioUsd = cashNotInPortfolioUsd;
    }

    public void MarkAsFinalFiling() => IsFinalFiling = true;

    public void ReplaceShareClasses(IEnumerable<ShareClass> shareClasses)
    {
        ArgumentNullException.ThrowIfNull(shareClasses);
        var unique = shareClasses
            .GroupBy(s => s.ClassId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        _shareClasses.Clear();
        _shareClasses.AddRange(unique);
    }

    public void ReplaceMonthlyReturns(IEnumerable<MonthlyReturn> monthlyReturns)
    {
        ArgumentNullException.ThrowIfNull(monthlyReturns);
        _monthlyReturns.Clear();
        _monthlyReturns.AddRange(monthlyReturns.OrderBy(r => r.Month).ThenBy(r => r.ClassId));
    }
}
