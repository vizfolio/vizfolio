using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Abstractions;

namespace Vizfolio.Api.Endpoints.Admin.Portfolios;

public sealed record RelinkLedgerResponse(int Linked);

public sealed class RelinkLedgerEndpoint : EndpointWithoutRequest<RelinkLedgerResponse>
{
    private readonly ILedgerRelinker _relinker;

    public RelinkLedgerEndpoint(ILedgerRelinker relinker)
    {
        _relinker = relinker;
    }

    public override void Configure()
    {
        Post("/admin/portfolios/relink");
        AllowAnonymous();
        Description(b => b
            .WithTags("Admin Imports")
            .Produces<RelinkLedgerResponse>(StatusCodes.Status200OK));
        Summary(s =>
        {
            s.Summary = "Re-resolve account ledger transactions to securities by ticker.";
            s.Description = "Useful after a securities extract has run — picks up any previously-unknown tickers and " +
                             "links the corresponding AccountTransaction rows to their Security.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var linked = await _relinker.RelinkAsync(ct);
        await Send.OkAsync(new RelinkLedgerResponse(linked), ct);
    }
}
