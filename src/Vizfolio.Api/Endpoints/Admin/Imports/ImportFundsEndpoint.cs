using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Api.Endpoints.Admin.Imports;

public sealed record ImportFundsRequest(IReadOnlyList<string>? SeriesIds, bool Force);

public sealed class ImportFundsEndpoint : Endpoint<ImportFundsRequest, ImportResult>
{
    private readonly IFundsImporter _importer;
    private readonly ImportRunGate _gate;

    public ImportFundsEndpoint(IFundsImporter importer, ImportRunGate gate)
    {
        _importer = importer;
        _gate = gate;
    }

    public override void Configure()
    {
        Post("/admin/imports/funds");
        AllowAnonymous();
        Description(b => b
            .WithTags("Admin Imports")
            .Produces<ImportResult>(StatusCodes.Status200OK)
            .Produces<ImportResult>(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Refresh fund snapshots from the open vizfolio/fund-extracts repository.";
            s.Description =
                "Fetches the funds.json manifest and, for each entry whose `latest_period` is newer than the most-recent stored snapshot, " +
                "downloads the gzipped snapshot, upserts the Fund, FundSnapshot, ShareClasses, MonthlyReturns and Holdings, " +
                "and auto-links holdings to existing Security rows by `issuer_cik`. " +
                "Use `seriesIds` to target specific funds, or `force: true` to re-import (and replace) snapshots already on disk.";
            s.ExampleRequest = new ImportFundsRequest(new[] { "S000012345" }, false);
            s.RequestParam(r => r.SeriesIds!,
                "Optional list of EDGAR series IDs (e.g. `S000012345`) to restrict the import to. Omit to import every fund in the manifest.");
            s.RequestParam(r => r.Force,
                "When true, re-imports targeted funds even if the manifest's `latest_period` is already on file. The matching snapshot (with all holdings) is replaced. Defaults to false.");
            s.Responses[StatusCodes.Status200OK] = "Import completed. The response summarises considered / upserted / skipped / failed counts.";
            s.Responses[StatusCodes.Status409Conflict] = "Another extracts import is currently in progress. Retry after it completes.";
        });
    }

    public override async Task HandleAsync(ImportFundsRequest req, CancellationToken ct)
    {
        if (!_gate.TryAcquire(out var handle))
        {
            await Send.ResponseAsync(new ImportResult(0, 0, 0, 0, [], TimeSpan.Zero), StatusCodes.Status409Conflict, ct);
            return;
        }

        using (handle)
        {
            var result = await _importer.ImportAsync(
                new FundsImportOptions(req?.SeriesIds, req?.Force ?? false), ct);
            await Send.OkAsync(result, ct);
        }
    }
}
