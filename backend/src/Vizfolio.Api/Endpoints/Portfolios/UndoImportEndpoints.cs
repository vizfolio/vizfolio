using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Services;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record UndoImportRequest(Guid PortfolioId, Guid ImportBatchId);

/// <summary>What undoing an import would remove or revert (dry run).</summary>
public sealed class GetImportUndoPreviewEndpoint : Endpoint<UndoImportRequest, ImportUndoSummary>
{
    private readonly IImportUndoService _undo;

    public GetImportUndoPreviewEndpoint(IImportUndoService undo)
    {
        _undo = undo;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/imports/{importBatchId}/undo-preview");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<ImportUndoSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Preview undoing an import: what would be removed or reverted. Nothing is written.";
            s.Description =
                "Counts the transactions and snapshots the import inserted, the stored rows it updated whose previous values would be restored, " +
                "the holdings and accounts it created that would be removed (only those nothing else uses), and the later imports into the same " +
                "accounts that would be replayed from their stored files. 404 for an unknown import, 409 if it's already undone.";
        });
    }

    public override async Task HandleAsync(UndoImportRequest req, CancellationToken ct)
        => await UndoImportResponses.SendAsync(HttpContext.Response, await _undo.PreviewAsync(req.PortfolioId, req.ImportBatchId, ct), ct);
}

/// <summary>Reverses an import. Takes no body: both ids come from the route.</summary>
public sealed class UndoImportEndpoint : EndpointWithoutRequest<ImportUndoSummary>
{
    private readonly IImportUndoService _undo;

    public UndoImportEndpoint(IImportUndoService undo)
    {
        _undo = undo;
    }

    public override void Configure()
    {
        Post("/portfolios/{portfolioId}/imports/{importBatchId}/undo");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<ImportUndoSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Undo an import: remove what the file added and restore what it changed.";
            s.Description =
                "In one database transaction: restores rows the import updated (where they still hold its values), deletes the transactions and " +
                "snapshots it inserted (never rows it skipped as duplicates of other imports), removes holdings and accounts it created that " +
                "nothing else uses, marks it undone, replays later imports into the same accounts from their stored files, then relinks holdings " +
                "and re-derives implied contributions. The import stays in the history as undone, and the same file can be imported again. " +
                "404 for an unknown import, 409 if it's already undone.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await _undo.UndoAsync(Route<Guid>("portfolioId"), Route<Guid>("importBatchId"), ct);
        await UndoImportResponses.SendAsync(HttpContext.Response, outcome, ct);
    }
}

internal static class UndoImportResponses
{
    public static Task SendAsync(HttpResponse response, ImportUndoOutcome outcome, CancellationToken ct)
        => outcome.Status switch
        {
            ImportUndoStatus.NotFound => response.SendNotFoundAsync(ct),
            ImportUndoStatus.AlreadyUndone => response.SendStatusCodeAsync(StatusCodes.Status409Conflict, ct),
            _ => response.SendOkAsync(outcome.Summary!, cancellation: ct),
        };
}
