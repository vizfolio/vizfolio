using Shouldly;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Domain;

public sealed class FundSnapshotTests
{
    private static readonly Guid SampleFundId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly SampleAsOf = new(2026, 3, 31);

    private static FundSnapshot CreateSnapshot() =>
        new(SampleFundId, SampleAsOf, "0001234567-26-000001", "https://example.com/filing");

    [Fact]
    public void Constructor_requires_fund_id()
    {
        Should.Throw<ArgumentException>(() =>
            new FundSnapshot(Guid.Empty, SampleAsOf, "acc", "url"));
    }

    [Theory]
    [InlineData("", "url")]
    [InlineData("acc", "")]
    public void Constructor_requires_source_filing_and_url(string filing, string url)
    {
        Should.Throw<ArgumentException>(() =>
            new FundSnapshot(SampleFundId, SampleAsOf, filing, url));
    }

    [Fact]
    public void SetFinancials_assigns_balance_sheet_values()
    {
        var snapshot = CreateSnapshot();

        snapshot.SetFinancials(1_000_000m, 1_200_000m, 200_000m, 50_000m);

        snapshot.NetAssetsUsd.ShouldBe(1_000_000m);
        snapshot.TotalAssetsUsd.ShouldBe(1_200_000m);
        snapshot.TotalLiabilitiesUsd.ShouldBe(200_000m);
        snapshot.CashNotInPortfolioUsd.ShouldBe(50_000m);
    }

    [Fact]
    public void MarkAsFinalFiling_sets_flag()
    {
        var snapshot = CreateSnapshot();

        snapshot.MarkAsFinalFiling();

        snapshot.IsFinalFiling.ShouldBeTrue();
    }

    [Fact]
    public void ReplaceShareClasses_drops_duplicates_by_class_id()
    {
        var snapshot = CreateSnapshot();
        var classes = new[]
        {
            new ShareClass("C000001", "Class A", "ACMEX", 0.0050m),
            new ShareClass("C000001", "Class A (dup)", "ACMEX", 0.0050m),
            new ShareClass("C000002", "Class I", "ACMIX", 0.0030m)
        };

        snapshot.ReplaceShareClasses(classes);

        snapshot.ShareClasses.Select(s => s.ClassId).ShouldBe(["C000001", "C000002"]);
        snapshot.ShareClasses.First().Name.ShouldBe("Class A");
    }

    [Fact]
    public void ReplaceMonthlyReturns_orders_chronologically()
    {
        var snapshot = CreateSnapshot();
        var returns = new[]
        {
            new MonthlyReturn(new DateOnly(2026, 3, 1), 0.02m, "C000001"),
            new MonthlyReturn(new DateOnly(2026, 1, 1), 0.01m, "C000001"),
            new MonthlyReturn(new DateOnly(2026, 2, 1), -0.005m, "C000001")
        };

        snapshot.ReplaceMonthlyReturns(returns);

        snapshot.MonthlyReturns.Select(r => r.Month).ShouldBe(
        [
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 1)
        ]);
    }
}
