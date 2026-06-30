namespace Vizfolio.Application.Performance.RateOfReturn;

public static class SimpleReturnCalculator
{
    public static decimal? Calculate(decimal beginningBalance, decimal endingBalance, decimal netCashFlow)
    {
        if (beginningBalance == 0m)
            return null;

        return (endingBalance - beginningBalance - netCashFlow) / beginningBalance;
    }
}
