namespace Vizfolio.Application.Portfolios;

public interface IMoneyWeightedReturnCalculator
{
    ReturnResult Compute(PerformanceComputationContext context);
}
