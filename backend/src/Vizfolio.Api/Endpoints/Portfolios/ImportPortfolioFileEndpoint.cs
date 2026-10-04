using System.Text.Json;
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

    /// <summary>
    /// After a <see cref="PortfolioImportStatus.NeedsAccountSelection"/> answer: a JSON array of
    /// <c>{ fileAccountNumber, accountId?, newAccount?: { name?, institutionCode?, accountNumber? } }</c> saying where each
    /// statement goes.
    /// </summary>
    public string? Assignments { get; set; }
}

public sealed class ImportPortfolioFileEndpoint : Endpoint<ImportPortfolioFileRequest, PortfolioImportResult>
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions AssignmentJson = new(JsonSerializerDefaults.Web);

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
            s.Summary = "Upload any supported broker file into a portfolio; each statement finds its account.";
            s.Description =
                "A statement that names its account (OFX/QFX `<INVACCTFROM>`) goes to the portfolio's account with that " +
                "`(InstitutionCode, AccountNumber)`, or a new one. A file without account details (e.g. the Vanguard report) goes " +
                "to the account that already holds its transactions (fingerprint routing, `Imports:Routing`). When that isn't " +
                "clear — or a new account number looks like an existing account — the response is `NeedsAccountSelection` with " +
                "candidates and nothing is written; send the file again with `Assignments`. " +
                "Returns 415 if no parser claims the file, 404 if the portfolio is missing, 413 if the upload exceeds the size cap, " +
                "and 400 if the assignments don't work.";
            s.Responses[StatusCodes.Status200OK] = "Import completed (response carries per-account results).";
            s.Responses[StatusCodes.Status404NotFound] = "Portfolio not found.";
            s.Responses[StatusCodes.Status413PayloadTooLarge] = "Upload exceeded the 10 MB cap.";
            s.Responses[StatusCodes.Status415UnsupportedMediaType] = "No registered parser claimed the file.";
            s.Responses[StatusCodes.Status422UnprocessableEntity] = "The file has no statements to import.";
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

        IReadOnlyList<StatementAssignment> assignments;
        try
        {
            assignments = string.IsNullOrWhiteSpace(req.Assignments)
                ? []
                : JsonSerializer.Deserialize<List<StatementAssignment>>(req.Assignments, AssignmentJson) ?? [];
        }
        catch (JsonException)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        await using var stream = req.File.OpenReadStream();
        var result = await _importer.ImportToPortfolioAsync(
            req.PortfolioId, stream, req.File.FileName, ct, req.SourceSystem,
            new ImportChoices { Assignments = assignments });

        var status = result.Status switch
        {
            PortfolioImportStatus.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.UnknownParser => StatusCodes.Status415UnsupportedMediaType,
            PortfolioImportStatus.PortfolioNotFound => StatusCodes.Status404NotFound,
            PortfolioImportStatus.FileHasNoAccountInfo => StatusCodes.Status422UnprocessableEntity,
            PortfolioImportStatus.InvalidAccountSelection => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status200OK,
        };
        await Send.ResponseAsync(result, status, ct);
    }
}
