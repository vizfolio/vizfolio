using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed class ImportPortfolioFileRequest
{
    public Guid PortfolioId { get; set; }
    public IFormFile File { get; set; } = default!;

    /// <summary>Optional parser override (e.g. "QFX"). Omit/blank to auto-detect the format.</summary>
    public string? SourceSystem { get; set; }
}

public sealed class ImportPortfolioFileEndpoint : Endpoint<ImportPortfolioFileRequest, PortfolioImportResult>
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IPortfolioImportService _importer;

    public ImportPortfolioFileEndpoint(IPortfolioImportService importer)
    {
        _importer = importer;
    }

    public override void Configure()
    {
        Post("/portfolios/{portfolioId}/imports");
        AllowAnonymous();
        AllowFileUploads();
        Description(b => b
            .WithTags("Portfolios")
            .Accepts<ImportPortfolioFileRequest>("multipart/form-data")
            .Produces<PortfolioImportResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces<PortfolioImportResult>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<PortfolioImportResult>(StatusCodes.Status422UnprocessableEntity));
        Summary(s =>
        {
            s.Summary = "Upload a broker file that carries its own account metadata (e.g. multi-account QFX) into a portfolio.";
            s.Description =
                "Used for files that identify each account inline (OFX/QFX `<INVACCTFROM>`/`<BANKACCTFROM>`). " +
                "For each statement in the file, the matching account in the portfolio is found by `(InstitutionCode, AccountNumber)` " +
                "or created on the fly with a placeholder name the user can rename later. " +
                "Returns 415 if no parser claims the file, 404 if the portfolio is missing, 413 if the upload exceeds the size cap, " +
                "and 422 if the file does not carry account metadata (use the account-scoped endpoint instead).";
            s.Responses[StatusCodes.Status200OK] = "Import completed (response carries per-account results).";
            s.Responses[StatusCodes.Status404NotFound] = "Portfolio not found.";
            s.Responses[StatusCodes.Status413PayloadTooLarge] = "Upload exceeded the 10 MB cap.";
            s.Responses[StatusCodes.Status415UnsupportedMediaType] = "No registered parser claimed the file.";
            s.Responses[StatusCodes.Status422UnprocessableEntity] = "File carries no account metadata; use the account-scoped import endpoint.";
        });
    }

    public override async Task HandleAsync(ImportPortfolioFileRequest req, CancellationToken ct)
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

        await using var stream = req.File.OpenReadStream();
        var result = await _importer.ImportToPortfolioAsync(req.PortfolioId, stream, req.File.FileName, ct, req.SourceSystem);

        var status = result.Status switch
        {
            PortfolioImportStatus.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.UnknownParser => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.PortfolioNotFound => StatusCodes.Status404NotFound,
            PortfolioImportStatus.FileHasNoAccountInfo => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status200OK,
        };
        await Send.ResponseAsync(result, status, ct);
    }
}
