using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Application.PortfolioImports.Abstractions;

public interface IPortfolioFileParser
{
    string SourceSystem { get; }

    Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken);

    Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken);
}
