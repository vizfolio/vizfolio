using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Application.Pricing.Models;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Api.Endpoints.Admin.Imports;

public sealed record ImportPriceHistoryRequest(
    IReadOnlyList<string>? Tickers,
    DateOnly? From,
    DateOnly? To,
    bool Force);

public sealed class ImportPriceHistoryEndpoint : Endpoint<ImportPriceHistoryRequest, ImportResult>
{
    private readonly IPriceHistoryImporter _importer;
    private readonly ImportRunGate _gate;

    public ImportPriceHistoryEndpoint(IPriceHistoryImporter importer, ImportRunGate gate)
    {
        _importer = importer;
        _gate = gate;
    }

    public override void Configure()
    {
        Post("/admin/imports/price-history");
        AllowAnonymous();
        Description(b => b
            .WithTags("Admin Imports")
            .Produces<ImportResult>(StatusCodes.Status200OK)
            .Produces<ImportResult>(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Fetch daily close prices for held holdings into the local price history.";
            s.Description =
                "Derives one price series per held security/symbol from the ledger and fetches raw daily " +
                "closes (and split events) from the configured price source — keyless Stooq by default, or " +
                "an API-key provider when configured. By default only the missing tail after the latest " +
                "stored date is fetched; pass `force: true` to re-fetch the full window, or `tickers` / " +
                "`from` / `to` to scope the run. Returns 409 if another import is already running.";
            s.ExampleRequest = new ImportPriceHistoryRequest(new[] { "AAPL", "MSFT" }, null, null, false);
            s.RequestParam(r => r.Tickers!, "Optional list of symbols (case-insensitive) to restrict the run to.");
            s.RequestParam(r => r.From!, "Optional start date (ISO). Defaults to each series' earliest ledger trade date.");
            s.RequestParam(r => r.To!, "Optional end date (ISO). Defaults to today (UTC).");
            s.RequestParam(r => r.Force, "When true, re-fetches the full window instead of only the missing tail. Defaults to false.");
            s.Responses[StatusCodes.Status200OK] = "Import completed. The response summarises considered / upserted / skipped / failed counts.";
            s.Responses[StatusCodes.Status409Conflict] = "Another import is currently in progress. Retry after it completes.";
        });
    }

    public override async Task HandleAsync(ImportPriceHistoryRequest req, CancellationToken ct)
    {
        if (!_gate.TryAcquire(out var handle))
        {
            await Send.ResponseAsync(ImportResult.Empty(TimeSpan.Zero), StatusCodes.Status409Conflict, ct);
            return;
        }

        using (handle)
        {
            var options = new PriceHistoryImportOptions(
                req?.Tickers, req?.From, req?.To, req?.Force ?? false);
            var result = await _importer.ImportAsync(options, ct);
            await Send.OkAsync(result, ct);
        }
    }
}
