namespace Vizfolio.Application.Portfolios;

public interface ITimeWeightedReturnCalculator
{
    ReturnResult Compute(PerformanceComputationContext context);
}
