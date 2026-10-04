using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Services;

namespace Vizfolio.Api.Endpoints.Portfolios;

/// <summary>GET the file an import stored, as it was uploaded.</summary>
public sealed class DownloadImportFileEndpoint : EndpointWithoutRequest
{
    private readonly IImportHistoryService _history;

    public DownloadImportFileEndpoint(IImportHistoryService history)
    {
        _history = history;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/imports/{importBatchId}/file");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "Download the file an import stored, exactly as it was uploaded.";
            s.Description =
                "Returns the original bytes as an attachment under the original file name and content type — for undone imports too. " +
                "404 when the import isn't in the portfolio, or it predates stored files (`hasStoredFile: false` in the history).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var file = await _history.GetFileAsync(Route<Guid>("portfolioId"), Route<Guid>("importBatchId"), ct);
        if (file is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.BytesAsync(file.Content, file.FileName, file.ContentType, cancellation: ct);
    }
}
