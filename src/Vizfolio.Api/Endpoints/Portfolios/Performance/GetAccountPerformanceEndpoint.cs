using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Performance;

namespace Vizfolio.Api.Endpoints.Portfolios.Performance;

public sealed class GetAccountPerformanceRequest
{
    public Guid PortfolioId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public PerformanceGranularity? Granularity { get; set; }
}

public sealed class GetAccountPerformanceEndpoint
    : Endpoint<GetAccountPerformanceRequest, PerformanceResponse>
{
    private readonly IAppDbContext _db;
    private readonly IPerformanceCalculator _calculator;

    public GetAccountPerformanceEndpoint(IAppDbContext db, IPerformanceCalculator calculator)
    {
        _db = db;
        _calculator = calculator;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/performance");
        AllowAnonymous();
        Description(b => b
            .WithTags("Performance")
            .Produces<PerformanceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "Performance summary and time series for a single account.";
            s.Description = "Defaults: from = (to - 1 year), to = today, granularity = Monthly. " +
                            "Daily granularity is capped at 366 days.";
        });
    }

    public override async Task HandleAsync(GetAccountPerformanceRequest req, CancellationToken ct)
    {
        var resolved = PerformanceRequestValidation.Resolve(
            req.From, req.To, req.Granularity, out var error);
        if (resolved is null)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var accountInPortfolio = await _db.Accounts.AsNoTracking()
            .AnyAsync(a => a.AccountId == req.AccountId && a.PortfolioId == req.PortfolioId, ct);
        if (!accountInPortfolio)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var calcRequest = new PerformanceRequest(
            PerformanceScope.Account, req.AccountId,
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
