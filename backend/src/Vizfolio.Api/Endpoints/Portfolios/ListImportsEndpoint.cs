using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Services;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record ListImportsRequest(Guid PortfolioId);

/// <summary>The portfolio's import history, newest first.</summary>
public sealed class ListImportsEndpoint : Endpoint<ListImportsRequest, ImportHistory>
{
    private readonly IImportHistoryService _history;

    public ListImportsEndpoint(IImportHistoryService history)
    {
        _history = history;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/imports");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<ImportHistory>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "List the portfolio's imports (uploaded files), newest first.";
            s.Description =
                "Each import lists the file, its detected format, when it was imported (and reprocessed or undone), what it did to each account, " +
                "and the warnings the parser raised. `transactionsImportedBeforeHistory` counts rows imported before imports were recorded: " +
                "they belong to no import and can't be undone.";
        });
    }

    public override async Task HandleAsync(ListImportsRequest req, CancellationToken ct)
    {
        var history = await _history.ListAsync(req.PortfolioId, ct);
        if (history is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(history, ct);
    }
}
