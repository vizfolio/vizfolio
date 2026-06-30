namespace Vizfolio.Application.Performance;

public sealed record PerformanceRequest(
    PerformanceScope Scope,
    Guid ScopeId,
    DateOnly From,
    DateOnly To,
    PerformanceGranularity Granularity);
