using FastEndpoints;

namespace Vizfolio.Api.Endpoints.Health;

public sealed record HealthResponse(string Status, DateTimeOffset Timestamp);

public sealed class GetHealthEndpoint : EndpointWithoutRequest<HealthResponse>
{
    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
        Description(b => b
            .WithTags("Diagnostics")
            .Produces<HealthResponse>(StatusCodes.Status200OK));
        Summary(s =>
        {
            s.Summary = "Liveness probe for the Vizfolio API.";
            s.Description = "Returns a constant `Healthy` status plus the server's current UTC timestamp. Intended for load-balancer / orchestrator probes.";
            s.Responses[StatusCodes.Status200OK] = "Service is up.";
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        return Send.OkAsync(new HealthResponse("Healthy", DateTimeOffset.UtcNow), ct);
    }
}
