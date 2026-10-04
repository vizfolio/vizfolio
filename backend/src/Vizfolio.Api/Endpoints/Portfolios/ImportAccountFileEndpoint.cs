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

    /// <summary>Optional parser override (e.g. "QFX"). Omit/blank to auto-detect the format.</summary>
    public string? SourceSystem { get; set; }
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
            .Produces<PortfolioImportResult>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<PortfolioImportResult>(StatusCodes.Status422UnprocessableEntity));
        Summary(s =>
        {
            s.Summary = "Upload a broker file (QFX, Vanguard report, ...) to ingest its transactions into the account ledger.";
            s.Description =
                "The format is auto-detected: each registered parser is offered the file from highest priority to lowest, and the first that recognises it processes the file. " +
                "Pass an optional `sourceSystem` form field (e.g. `QFX`) to force a specific parser and skip detection; an unknown value returns 415. " +
                "Re-uploading the exact same file is recognised by its SHA-256 and returns `status: AlreadyImported` with the earlier import's summary, writing nothing. " +
                "Other files are deduplicated row by row by `(account, source, externalId)` and, across sources, by economic fingerprint; rows already stored from the same source are updated to the parser's current mapping. " +
                "A file that names its accounts (e.g. a QFX) contributes only the statement whose account number matches this account (separators and case ignored); its other statements are skipped with an `OtherAccountSkipped` warning. " +
                "Every upload is recorded as an import batch that can be listed and undone (GET /portfolios/{portfolioId}/imports). " +
                "Returns 415 if no parser claims the file, 404 if the portfolio or account is missing, 413 if the upload exceeds the size cap, " +
                "and 422 with `status: AccountMismatch` (and the file's masked `fileAccountNumbers`) if the file names accounts and none is this one.";
            s.Responses[StatusCodes.Status200OK] = "Import completed (may contain row-level failures in the response body).";
            s.Responses[StatusCodes.Status404NotFound] = "Portfolio or account not found.";
            s.Responses[StatusCodes.Status413PayloadTooLarge] = "Upload exceeded the 10 MB cap.";
            s.Responses[StatusCodes.Status415UnsupportedMediaType] = "No registered parser claimed the file.";
            s.Responses[StatusCodes.Status422UnprocessableEntity] = "The file is for other accounts; import it into the portfolio instead.";
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
        var result = await _importer.ImportToAccountAsync(req.AccountId, stream, req.File.FileName, ct, req.SourceSystem);

        var status = result.Status switch
        {
            PortfolioImportStatus.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.UnknownParser => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.AccountNotFound => StatusCodes.Status404NotFound,
            PortfolioImportStatus.FileHasAccountInfo => StatusCodes.Status422UnprocessableEntity,
            PortfolioImportStatus.AccountMismatch => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status200OK,
        };
        await Send.ResponseAsync(result, status, ct);
    }
}
