using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Performance;

namespace Vizfolio.Api.Endpoints.Portfolios.Performance;

public sealed class GetPortfolioPerformanceRequest
{
    public Guid PortfolioId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public PerformanceGranularity? Granularity { get; set; }
}

public sealed class GetPortfolioPerformanceEndpoint
    : Endpoint<GetPortfolioPerformanceRequest, PerformanceResponse>
{
    private readonly IPerformanceCalculator _calculator;

    public GetPortfolioPerformanceEndpoint(IPerformanceCalculator calculator)
    {
        _calculator = calculator;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/performance");
        AllowAnonymous();
        Description(b => b
            .WithTags("Performance")
            .Produces<PerformanceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "Performance summary and time series for a portfolio.";
            s.Description = "Aggregates across all accounts in the portfolio. " +
                            "Defaults: from = (to - 1 year), to = today, granularity = Monthly. " +
                            "Daily granularity is capped at 366 days.";
        });
    }

    public override async Task HandleAsync(GetPortfolioPerformanceRequest req, CancellationToken ct)
    {
        var resolved = PerformanceRequestValidation.Resolve(
            req.From, req.To, req.Granularity, out var error);
        if (resolved is null)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var calcRequest = new PerformanceRequest(
            PerformanceScope.Portfolio, req.PortfolioId,
            resolved.Value.From, resolved.Value.To, resolved.Value.Granularity);

        var result = await _calculator.CalculateAsync(calcRequest, ct);
        if (result is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(result.ToResponse(), ct);
    }
}
