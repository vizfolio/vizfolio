using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Api.Endpoints.Admin.Imports;

public sealed record ImportAllRequest(bool Force);

public sealed record ImportAllResponse(ImportResult Securities, ImportResult Funds, ImportResult MoneyMarketFunds, int Relinked);

public sealed class ImportAllEndpoint : Endpoint<ImportAllRequest, ImportAllResponse>
{
    private readonly ISecuritiesImporter _securities;
    private readonly IFundsImporter _funds;
    private readonly IMoneyMarketFundsImporter _moneyMarketFunds;
    private readonly IHoldingRelinker _relinker;
    private readonly ILedgerRelinker _ledgerRelinker;
    private readonly ImportRunGate _gate;

    public ImportAllEndpoint(
        ISecuritiesImporter securities,
        IFundsImporter funds,
        IMoneyMarketFundsImporter moneyMarketFunds,
        IHoldingRelinker relinker,
        ILedgerRelinker ledgerRelinker,
        ImportRunGate gate)
    {
        _securities = securities;
        _funds = funds;
        _moneyMarketFunds = moneyMarketFunds;
        _relinker = relinker;
        _ledgerRelinker = ledgerRelinker;
        _gate = gate;
    }

    public override void Configure()
    {
        Post("/admin/imports/all");
        AllowAnonymous();
        Description(b => b
            .WithTags("Admin Imports")
            .Produces<ImportAllResponse>(StatusCodes.Status200OK)
            .Produces<ImportAllResponse>(StatusCodes.Status409Conflict));
        Summary(s =>
        {
            s.Summary = "Run a complete extracts refresh: securities first, then funds, then holding re-link.";
            s.Description =
                "Convenience endpoint that chains the three import phases under a single run-gate. " +
                "Securities are imported before funds so that newly-added Security rows are available for auto-linking. " +
                "After both imports, a re-link pass attaches any previously-unlinked holdings to securities by `issuer_cik`. " +
                "The optional `force` flag is forwarded to both importers.";
            s.ExampleRequest = new ImportAllRequest(false);
            s.RequestParam(r => r.Force,
                "When true, both the securities and funds imports bypass their delta checks and re-process every entry. Defaults to false.");
            s.Responses[StatusCodes.Status200OK] = "All three phases completed. The response contains the securities and funds ImportResult and the re-link count.";
            s.Responses[StatusCodes.Status409Conflict] = "Another extracts import is currently in progress. Retry after it completes.";
        });
    }

    public override async Task HandleAsync(ImportAllRequest req, CancellationToken ct)
    {
        if (!_gate.TryAcquire(out var handle))
        {
            var empty = ImportResult.Empty(TimeSpan.Zero);
            await Send.ResponseAsync(new ImportAllResponse(empty, empty, empty, 0), StatusCodes.Status409Conflict, ct);
            return;
        }

        using (handle)
        {
            var force = req?.Force ?? false;
            var secResult = await _securities.ImportAsync(new SecuritiesImportOptions(Force: force), ct);
            var fundResult = await _funds.ImportAsync(new FundsImportOptions(Force: force), ct);
            var moneyMarketResult = await _moneyMarketFunds.ImportAsync(ct);
            var relinked = await _relinker.RelinkAsync(ct);
            // New reference data may recognise tickers that account holdings were created without.
            await _ledgerRelinker.RelinkAsync(ct);
            await Send.OkAsync(new ImportAllResponse(secResult, fundResult, moneyMarketResult, relinked), ct);
        }
    }
}
