using Shouldly;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Tests.Portfolios;

public sealed class ReturnRatesTests
{
    [Fact]
    public void A_total_over_several_years_is_annualized_to_the_rate_that_compounds_to_it()
        // 50% over 5 years is ~8.4% a year (1.0845^5 ≈ 1.5).
        => ReturnRates.Annualize(0.50m, 365 * 5)!.Value.ShouldBe(0.0845m, tolerance: 0.0001m);

    [Fact]
    public void A_per_year_rate_compounds_back_to_the_same_total()
    {
        var annual = ReturnRates.Annualize(0.35m, 1000);

        ReturnRates.Compound(annual, 1000)!.Value.ShouldBe(0.35m, tolerance: 0.000001m);
    }

    [Fact]
    public void Periods_under_a_year_are_never_annualized()
        => ReturnRates.Annualize(0.03m, 14).ShouldBeNull();

    [Fact]
    public void A_total_loss_has_no_per_year_rate()
        => ReturnRates.Annualize(-1m, 730).ShouldBeNull();

    [Fact]
    public void Complete_fills_both_forms_of_a_total_over_two_years()
    {
        var result = ReturnRates.Complete(new ReturnResult(0.21m, "DailyValuedTWR", ReturnResult.PeriodBasis, null), 730);

        result.PeriodRate.ShouldBe(0.21m);
        result.AnnualizedRate!.Value.ShouldBe(0.10m, tolerance: 0.000001m);
    }

    [Fact]
    public void Complete_fills_the_total_of_a_per_year_rate_and_keeps_one_already_given()
    {
        var annualized = new ReturnResult(0.10m, "XIRR", ReturnResult.AnnualizedBasis, null);

        ReturnRates.Complete(annualized, 730).PeriodRate!.Value.ShouldBe(0.21m, tolerance: 0.000001m);
        ReturnRates.Complete(annualized with { PeriodRate = 0.10m }, 730).PeriodRate.ShouldBe(0.10m);
    }

    [Fact]
    public void Complete_clears_both_forms_when_there_is_no_rate()
    {
        var result = ReturnRates.Complete(
            new ReturnResult(null, "XIRR", ReturnResult.AnnualizedBasis, "UnvaluedTransfer") { PeriodRate = 0.2m, AnnualizedRate = 0.1m },
            730);

        result.PeriodRate.ShouldBeNull();
        result.AnnualizedRate.ShouldBeNull();
    }
}
