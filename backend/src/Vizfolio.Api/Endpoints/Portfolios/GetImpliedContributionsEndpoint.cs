using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Portfolios;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetImpliedContributionsRequest(Guid PortfolioId, Guid AccountId);

public sealed record ImpliedContributionRowResponse(
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    string Type,
    string? SourceType,
    string? Ticker,
    decimal? Quantity,
    decimal Amount,
    decimal CashEffect);

public sealed record ImpliedContributionResponse(
    DateOnly Date, decimal Amount, decimal CashBefore, IReadOnlyList<ImpliedContributionRowResponse> DayRows);

public sealed record ImpliedContributionYearResponse(int Year, decimal Amount, int Count);

public sealed record ImpliedContributionPreviewResponse(
    Guid AccountId,
    decimal Tolerance,
    decimal TotalAmount,
    decimal EndingCash,
    IReadOnlyList<ImpliedContributionYearResponse> ByYear,
    IReadOnlyList<ImpliedContributionResponse> Contributions);

/// <summary>Dry run of the contributions an account's ledger implies but never recorded.</summary>
public sealed class GetImpliedContributionsEndpoint
    : Endpoint<GetImpliedContributionsRequest, ImpliedContributionPreviewResponse>
{
    private readonly IImpliedContributionService _service;

    public GetImpliedContributionsEndpoint(IImpliedContributionService service)
    {
        _service = service;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/implied-contributions");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<ImpliedContributionPreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "Preview contributions implied by purchases with no recorded deposit (dry run).";
            s.Description =
                "Rolls the account's cash forward from zero, a day at a time by settlement date (trade date " +
                "when there is none); a day that closes below zero " +
                "(beyond a $1 tolerance) implies an unrecorded contribution of the shortfall. Nothing is written.";
        });
    }

    public override async Task HandleAsync(GetImpliedContributionsRequest req, CancellationToken ct)
    {
        var preview = await _service.PreviewForAccountAsync(req.PortfolioId, req.AccountId, ct);
        if (preview is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(ToResponse(preview), ct);
    }

    private static ImpliedContributionPreviewResponse ToResponse(ImpliedContributionPreview p) => new(
        p.AccountId,
        p.Tolerance,
        p.TotalAmount,
        p.EndingCash,
        p.ByYear.Select(y => new ImpliedContributionYearResponse(y.Year, y.Amount, y.Count)).ToList(),
        p.Contributions.Select(c => new ImpliedContributionResponse(
            c.Date,
            c.Amount,
            c.CashBefore,
            c.DayRows.Select(m => new ImpliedContributionRowResponse(
                m.Row.TradeDate,
                m.Row.SettlementDate,
                m.Row.Type.ToString(), m.Row.SourceType, m.Row.Ticker, m.Row.Quantity, m.Row.Amount,
                m.CashEffect)).ToList())).ToList());
}
