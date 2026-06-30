using Shouldly;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Api.Tests.Domain;

public sealed class MonthlyReturnTests
{
    [Fact]
    public void Constructor_anchors_month_to_first_day()
    {
        var monthlyReturn = new MonthlyReturn(new DateOnly(2026, 3, 28), 0.012m, "C000001");

        monthlyReturn.Month.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Constructor_normalizes_blank_class_id_to_null()
    {
        var monthlyReturn = new MonthlyReturn(new DateOnly(2026, 3, 1), 0.012m, "   ");

        monthlyReturn.ClassId.ShouldBeNull();
    }

    [Fact]
    public void Constructor_preserves_negative_returns()
    {
        var monthlyReturn = new MonthlyReturn(new DateOnly(2026, 3, 1), -0.025m, "C000001");

        monthlyReturn.ReturnPct.ShouldBe(-0.025m);
    }
}
