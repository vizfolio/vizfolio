namespace Vizfolio.Application.Performance;

public interface IPerformanceCalculator
{
    Task<PerformanceResult?> CalculateAsync(PerformanceRequest request, CancellationToken ct);
}
