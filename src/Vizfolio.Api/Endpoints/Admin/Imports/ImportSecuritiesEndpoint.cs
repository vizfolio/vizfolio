using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Api.Endpoints.Admin.Imports;

public sealed record ImportSecuritiesRequest(IReadOnlyList<string>? Tickers, bool Force);

public sealed class ImportSecuritiesEndpoint : Endpoint<ImportSecuritiesRequest, ImportResult>
{
    private readonly ISecuritiesImporter _importer;
    private readonly ImportRunGate _gate;

    public ImportSecuritiesEndpoint(ISecuritiesImporter importer, ImportRunGate gate)
    {
        _importer = importer;
        _gate = gate;
    }

    public override void Configure()
    {
        Post("/admin/imports/securities");
        AllowAnonymous();
        Description(b => b
            .WithTags("Admin Imports")
            .Produces<ImportResult>(StatusCodes.Status200OK)
            .Produces<ImportResult>(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Refresh the securities reference data from the open vizfolio/securities-extracts repository.";
            s.Description =
                "Fetches the by_ticker.json manifest, then upserts per-CIK security profiles. " +
                "By default only entries whose source `edgar_fetched_at` is newer than the stored value are imported. " +
                "Supply a `tickers` filter to scope the run, or `force: true` to bypass the delta check. " +
                "Returns 409 if another import (manual or scheduled) is already running.";
            s.ExampleRequest = new ImportSecuritiesRequest(new[] { "AAPL", "MSFT" }, false);
            s.RequestParam(r => r.Tickers!,
                "Optional list of tickers (case-insensitive) to restrict the import to. Omit to import the entire manifest.");
            s.RequestParam(r => r.Force,
                "When true, re-imports every targeted security regardless of whether the extract is newer. Defaults to false.");
            s.Responses[StatusCodes.Status200OK] = "Import completed. The response summarises considered / upserted / skipped / failed counts.";
            s.Responses[StatusCodes.Status409Conflict] = "Another extracts import is currently in progress. Retry after it completes.";
        });
    }

    public override async Task HandleAsync(ImportSecuritiesRequest req, CancellationToken ct)
    {
        if (!_gate.TryAcquire(out var handle))
        {
            await Send.ResponseAsync(new ImportResult(0, 0, 0, 0, [], TimeSpan.Zero), StatusCodes.Status409Conflict, ct);
            return;
        }

        using (handle)
        {
            var result = await _importer.ImportAsync(
                new SecuritiesImportOptions(req?.Tickers, req?.Force ?? false), ct);
            await Send.OkAsync(result, ct);
        }
    }
}
