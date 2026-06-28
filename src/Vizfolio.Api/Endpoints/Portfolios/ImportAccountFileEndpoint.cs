using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed class ImportAccountFileRequest
{
    public Guid PortfolioId { get; set; }
    public Guid AccountId { get; set; }
    public IFormFile File { get; set; } = default!;
}

public sealed class ImportAccountFileEndpoint : Endpoint<ImportAccountFileRequest, PortfolioImportResult>
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IPortfolioImportService _importer;
    private readonly IAppDbContext _db;

    public ImportAccountFileEndpoint(IPortfolioImportService importer, IAppDbContext db)
    {
        _importer = importer;
        _db = db;
    }

    public override void Configure()
    {
        Post("/portfolios/{portfolioId}/accounts/{accountId}/imports");
        AllowAnonymous();
        AllowFileUploads();
        Description(b => b
            .WithTags("Portfolios")
            .Accepts<ImportAccountFileRequest>("multipart/form-data")
            .Produces<PortfolioImportResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces<PortfolioImportResult>(StatusCodes.Status415UnsupportedMediaType));
        Summary(s =>
        {
            s.Summary = "Upload a broker file (QFX, CSV, ...) to ingest its transactions into the account ledger.";
            s.Description =
                "Each registered parser is offered the file in registration order; the first one that recognises the format processes it. " +
                "Transactions are deduplicated by `(account, source, externalId)`, so re-uploading the same file is a no-op. " +
                "Re-running this is also safe: importing the same file twice yields `Inserted: 0, Skipped: N`. " +
                "Returns 415 if no parser claims the file, 404 if the portfolio or account is missing, 413 if the upload exceeds the size cap.";
            s.Responses[StatusCodes.Status200OK] = "Import completed (may contain row-level failures in the response body).";
            s.Responses[StatusCodes.Status404NotFound] = "Portfolio or account not found.";
            s.Responses[StatusCodes.Status413PayloadTooLarge] = "Upload exceeded the 10 MB cap.";
            s.Responses[StatusCodes.Status415UnsupportedMediaType] = "No registered parser claimed the file.";
        });
    }

    public override async Task HandleAsync(ImportAccountFileRequest req, CancellationToken ct)
    {
        if (req.File is null || req.File.Length == 0)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (req.File.Length > MaxUploadBytes)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status413PayloadTooLarge, ct);
            return;
        }

        var accountExists = await _db.Accounts.AsNoTracking()
            .AnyAsync(a => a.PortfolioId == req.PortfolioId && a.AccountId == req.AccountId, ct);
        if (!accountExists)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await using var stream = req.File.OpenReadStream();
        var result = await _importer.ImportAsync(req.AccountId, stream, req.File.FileName, ct);

        var status = result.Status switch
        {
            PortfolioImportStatus.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.AccountNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status200OK,
        };
        await Send.ResponseAsync(result, status, ct);
    }
}
