using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Application.PortfolioImports.Abstractions;

/// <summary>
/// A pluggable importer for one broker file format. Register an implementation in
/// <c>DependencyInjection.AddApplication</c> and the pipeline will pick it up automatically —
/// either by auto-detection (<see cref="CanParseAsync"/>) or by an explicit
/// <see cref="SourceSystem"/> override from the UI. See <c>docs/performance-api.md</c>.
/// </summary>
public interface IPortfolioFileParser
{
    /// <summary>
    /// Stable machine key for this format (e.g. <c>"QFX"</c>). Used both as the transaction
    /// dedup source key and as the value the UI sends to force this parser.
    /// </summary>
    string SourceSystem { get; }

    /// <summary>Human-readable label shown in the import "Format" dropdown.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Auto-detect ordering: parsers are offered the file from highest to lowest priority, so
    /// provider-specific parsers should sit above generic ones. Ties fall back to registration order.
    /// </summary>
    int Priority { get; }

    /// <summary>File extensions this parser handles (e.g. <c>[".qfx", ".ofx"]</c>), used to build the UI accept hint.</summary>
    IReadOnlyCollection<string> FileExtensions { get; }

    Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken);

    Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken);
}
