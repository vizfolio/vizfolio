using Shouldly;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class PerformanceSeriesBuilderTests
{
    private static PerformanceBalanceResult Complete(decimal value) => new(value, true, null, 1, 0);

    private static PerformanceBalanceResult Incomplete() => new(0m, false, null, 0, 1);

    [Theory]
    [InlineData("2025-01-01", "2025-03-31", PerformanceSeriesInterval.Weekly)]
    [InlineData("2025-01-01", "2026-01-01", PerformanceSeriesInterval.Monthly)]
    [InlineData("2016-01-01", "2025-12-31", PerformanceSeriesInterval.Monthly)]
    [InlineData("2011-04-13", "2026-09-27", PerformanceSeriesInterval.Quarterly)]
    public void Interval_widens_with_the_length_of_the_period(string from, string to, PerformanceSeriesInterval expected)
    {
        PerformanceSeriesBuilder.IntervalFor(DateOnly.Parse(from), DateOnly.Parse(to)).ShouldBe(expected);
    }

    [Fact]
    public void Opens_at_the_close_of_the_day_before_from_then_marks_each_month_end_and_closes_at_to()
    {
        var from = new DateOnly(2025, 1, 15);
        var to = new DateOnly(2025, 5, 10);

        var series = PerformanceSeriesBuilder.Build(from, to, _ => Complete(1m), []);

        series.Interval.ShouldBe(PerformanceSeriesInterval.Monthly);
        series.Points.Select(p => p.Date).ShouldBe(new[]
        {
            new DateOnly(2025, 1, 14), // opening = starting balance
            new DateOnly(2025, 1, 31),
            new DateOnly(2025, 2, 28),
            new DateOnly(2025, 3, 31),
            new DateOnly(2025, 4, 30),
            new DateOnly(2025, 5, 10), // clamped to `to` = ending balance
        });
    }

    [Fact]
    public void Quarterly_points_fall_on_quarter_ends()
    {
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2010, 2, 1), new DateOnly(2021, 1, 20), _ => Complete(1m), []);

        series.Interval.ShouldBe(PerformanceSeriesInterval.Quarterly);
        series.Points.Skip(1).Take(3).Select(p => p.Date).ShouldBe(new[]
        {
            new DateOnly(2010, 3, 31), new DateOnly(2010, 6, 30), new DateOnly(2010, 9, 30),
        });
        series.Points[^1].Date.ShouldBe(new DateOnly(2021, 1, 20));
    }

    [Fact]
    public void Each_point_values_its_own_date_with_the_supplied_balance()
    {
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), d => Complete(d.Month * 100m), []);

        // Opening is 2024-12-31 (month 12), then each month end.
        series.Points.Select(p => p.Value).ShouldBe(new decimal?[] { 1200m, 100m, 200m, 300m, 400m, 500m, 600m });
    }

    [Fact]
    public void Flows_are_bucketed_into_the_point_they_precede_and_sum_to_the_period_total()
    {
        var from = new DateOnly(2025, 1, 1);
        var flows = new[]
        {
            new CashFlow(new DateOnly(2025, 1, 1), 1000m),   // first day → first month, not the opening
            new CashFlow(new DateOnly(2025, 1, 31), -200m),  // on the month end → that month
            new CashFlow(new DateOnly(2025, 2, 1), 500m),
            new CashFlow(new DateOnly(2025, 2, 14), -50m),
        };

        var series = PerformanceSeriesBuilder.Build(from, new DateOnly(2025, 12, 31), _ => Complete(1m), flows);

        var opening = series.Points[0];
        (opening.Deposits, opening.Withdrawals).ShouldBe((0m, 0m));
        (series.Points[1].Deposits, series.Points[1].Withdrawals).ShouldBe((1000m, -200m));
        (series.Points[2].Deposits, series.Points[2].Withdrawals).ShouldBe((500m, -50m));
        series.Points.Sum(p => p.Deposits + p.Withdrawals).ShouldBe(1250m);
    }

    [Fact]
    public void A_point_that_cannot_be_fully_valued_is_a_gap_not_a_guess()
    {
        var gapDay = new DateOnly(2025, 2, 28);
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
            d => d == gapDay ? Incomplete() : Complete(10m), []);

        series.Points.Single(p => p.Date == gapDay).Value.ShouldBeNull();
        series.Points.Where(p => p.Date != gapDay).ShouldAllBe(p => p.Value == 10m);
    }

    [Fact]
    public void A_single_day_period_has_its_opening_and_closing_point()
    {
        var day = new DateOnly(2025, 6, 2);

        var series = PerformanceSeriesBuilder.Build(day, day, _ => Complete(1m), []);

        series.Points.Select(p => p.Date).ShouldBe(new[] { day.AddDays(-1), day });
    }

    [Fact]
    public void The_opening_point_has_zero_return_and_zero_gain()
    {
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), _ => Complete(100m), [], (_, _) => 0.5m);

        (series.Points[0].CumulativeReturn, series.Points[0].InvestmentGain).ShouldBe((0m, 0m));
    }

    [Fact]
    public void Investment_gain_is_value_growth_not_explained_by_contributions()
    {
        // Opening 1000; a 500 deposit in January; value 1600 at Jan end → 100 gain, not 600.
        // A 200 withdrawal in February with value 1450 → 1450 − 1000 − (500 − 200) = 150.
        var from = new DateOnly(2025, 1, 1);
        var values = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2024, 12, 31)] = 1000m,
            [new DateOnly(2025, 1, 31)] = 1600m,
            [new DateOnly(2025, 2, 28)] = 1450m,
            [new DateOnly(2025, 3, 31)] = 1450m,
            [new DateOnly(2025, 4, 30)] = 1300m,
        };
        var flows = new[]
        {
            new CashFlow(new DateOnly(2025, 1, 10), 500m),
            new CashFlow(new DateOnly(2025, 2, 10), -200m),
        };

        var series = PerformanceSeriesBuilder.Build(from, new DateOnly(2025, 4, 30), d => Complete(values[d]), flows);

        // Losses show as the gain falling: April's 150 drop takes it to 0.
        series.Points.Select(p => p.InvestmentGain).ShouldBe(new decimal?[] { 0m, 100m, 150m, 150m, 0m });
    }

    [Fact]
    public void A_deposit_alone_is_not_investment_gain()
    {
        var from = new DateOnly(2025, 1, 1);
        var series = PerformanceSeriesBuilder.Build(
            from, new DateOnly(2025, 1, 31),
            d => Complete(d < from ? 1000m : 1500m),
            [new CashFlow(new DateOnly(2025, 1, 15), 500m)]);

        series.Points[^1].InvestmentGain.ShouldBe(0m);
    }

    [Fact]
    public void Cumulative_return_comes_from_the_supplied_strategy_for_each_point()
    {
        var calls = new List<(DateOnly Date, decimal Value)>();
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 30),
            d => Complete(d.Month * 10m),
            [],
            (date, balance) =>
            {
                calls.Add((date, balance.Value));
                return date.Month / 100m;
            });

        calls.ShouldBe(new[]
        {
            (new DateOnly(2025, 1, 31), 10m), (new DateOnly(2025, 2, 28), 20m),
            (new DateOnly(2025, 3, 31), 30m), (new DateOnly(2025, 4, 30), 40m),
        });
        series.Points.Select(p => p.CumulativeReturn).ShouldBe(new decimal?[] { 0m, 0.01m, 0.02m, 0.03m, 0.04m });
    }

    [Fact]
    public void Without_a_return_strategy_interior_returns_are_unknown()
    {
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), _ => Complete(1m), []);

        series.Points.Skip(1).ShouldAllBe(p => p.CumulativeReturn == null);
    }

    [Fact]
    public void Return_and_gain_are_gaps_when_the_point_cannot_be_fully_valued()
    {
        var gapDay = new DateOnly(2025, 2, 28);
        var series = PerformanceSeriesBuilder.Build(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 30),
            d => d == gapDay ? Incomplete() : Complete(10m), [], (_, _) => 0.1m);

        var gap = series.Points.Single(p => p.Date == gapDay);
        (gap.CumulativeReturn, gap.InvestmentGain).ShouldBe(((decimal?)null, (decimal?)null));
        series.Points[^1].CumulativeReturn.ShouldBe(0.1m);
    }

    [Fact]
    public void An_incomplete_opening_leaves_every_return_and_gain_unknown()
    {
        var from = new DateOnly(2025, 1, 1);
        var series = PerformanceSeriesBuilder.Build(
            from, new DateOnly(2025, 3, 31), d => d < from ? Incomplete() : Complete(10m), [], (_, _) => 0.1m);

        series.Points.ShouldAllBe(p => p.CumulativeReturn == null && p.InvestmentGain == null);
    }
}
