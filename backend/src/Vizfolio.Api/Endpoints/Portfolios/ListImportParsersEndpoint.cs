using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Vizfolio.Application.PortfolioImports.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

/// <summary>Describes one available import parser so the UI can offer it as a "Format" override.</summary>
public sealed record ImportParserResponse(
    string SourceSystem,
    string DisplayName,
    IReadOnlyCollection<string> FileExtensions);

public sealed class ListImportParsersEndpoint : EndpointWithoutRequest<IReadOnlyList<ImportParserResponse>>
{
    private readonly IEnumerable<IPortfolioFileParser> _parsers;

    public ListImportParsersEndpoint(IEnumerable<IPortfolioFileParser> parsers)
    {
        _parsers = parsers;
    }

    public override void Configure()
    {
        Get("/imports/parsers");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<IReadOnlyList<ImportParserResponse>>(StatusCodes.Status200OK));
        Summary(s => s.Summary =
            "List the registered import parsers (auto-detect order) so the UI can offer a Format override.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Highest priority first — the same order auto-detection offers them in.
        var results = _parsers
            .OrderByDescending(p => p.Priority)
            .Select(p => new ImportParserResponse(p.SourceSystem, p.DisplayName, p.FileExtensions))
            .ToList();

        await Send.OkAsync(results, ct);
    }
}
