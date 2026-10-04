using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed class ReprocessImportsRequest
{
    /// <summary>Only this portfolio's imports; omit for every portfolio.</summary>
    [QueryParam]
    public Guid? PortfolioId { get; set; }
}

/// <summary>Re-parses stored import files with the current parsers.</summary>
public sealed class ReprocessImportsEndpoint : Endpoint<ReprocessImportsRequest, ReprocessResult>
{
    private readonly IPortfolioImportService _imports;

    public ReprocessImportsEndpoint(IPortfolioImportService imports)
    {
        _imports = imports;
    }

    public override void Configure()
    {
        Post("/imports/reprocess");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<ReprocessResult>(StatusCodes.Status200OK));
        Summary(s =>
        {
            s.Summary = "Re-parse every stored import file with the current parsers, so parser fixes reach existing data.";
            s.Description =
                "Each active import's stored file is parsed again (oldest first) with the parser that imported it: rows the parser used to drop are " +
                "added, and stored rows it now maps differently (type, amount, quantity, price, settlement date, label, settlement-fund flag) are " +
                "updated in place, recorded on that import so undoing it restores them. Nothing is cleared. Imports made before files were stored " +
                "can't be reprocessed; re-upload those files once. Pass `?portfolioId=` to limit it to one portfolio.";
        });
    }

    public override async Task HandleAsync(ReprocessImportsRequest req, CancellationToken ct)
        => await Send.OkAsync(await _imports.ReprocessAsync(req.PortfolioId, batchIds: null, ct), ct);
}
