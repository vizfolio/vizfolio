using FastEndpoints;

namespace Vizfolio.Api.Endpoints.Health;

public sealed record HealthResponse(string Status, DateTimeOffset Timestamp);

public sealed class GetHealthEndpoint : EndpointWithoutRequest<HealthResponse>
{
    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
        Description(b => b.WithTags("Diagnostics"));
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        return Send.OkAsync(new HealthResponse("Healthy", DateTimeOffset.UtcNow), ct);
    }
}
