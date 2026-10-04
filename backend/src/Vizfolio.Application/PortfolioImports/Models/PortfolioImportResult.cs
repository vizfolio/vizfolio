namespace Vizfolio.Application.PortfolioImports.Models;

/// <summary>
/// Outcome of an import. Values are serialized as numbers: append new ones at the end (the frontend mirrors the
/// order in core/api/models/imports.models.ts).
/// </summary>
public enum PortfolioImportStatus
{
    Success,
    UnsupportedFormat,
    UnknownParser,
    AccountNotFound,
    PortfolioNotFound,
    FileHasNoAccountInfo,
    FileHasAccountInfo,

    /// <summary>This exact file was already imported here (and not undone); nothing was written.</summary>
    AlreadyImported,

    /// <summary>The file names its accounts, and none is the account it was uploaded to.</summary>
    AccountMismatch,
}

public sealed record PortfolioImportFailure(string Key, string Reason);

public sealed record PortfolioImportResult(
    PortfolioImportStatus Status,
    string? SourceSystem,
    IReadOnlyList<AccountImportResult> Accounts,
    TimeSpan Duration)
{
    /// <summary>The import's record (see GET /api/portfolios/{id}/imports); the one it repeats for <see cref="PortfolioImportStatus.AlreadyImported"/>.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>When the batch was imported — for <see cref="PortfolioImportStatus.AlreadyImported"/>, the earlier import.</summary>
    public DateTimeOffset? ImportedAt { get; init; }

    /// <summary>Everything in the file that wasn't fully understood (unmapped labels, unknown aggregates, failed rows, …).</summary>
    public IReadOnlyList<ImportWarning> Warnings { get; init; } = [];

    /// <summary>For <see cref="PortfolioImportStatus.AccountMismatch"/>: the account numbers the file does contain.</summary>
    public IReadOnlyList<string> FileAccountNumbers { get; init; } = [];

    public static PortfolioImportResult UnsupportedFormat(TimeSpan duration) =>
        new(PortfolioImportStatus.UnsupportedFormat, null, Array.Empty<AccountImportResult>(), duration);

    /// <summary>The UI forced a specific parser via <c>sourceSystem</c>, but no registered parser owns that key.</summary>
    public static PortfolioImportResult UnknownParser(string requestedSourceSystem, TimeSpan duration) =>
        new(PortfolioImportStatus.UnknownParser, requestedSourceSystem, Array.Empty<AccountImportResult>(), duration);

    public static PortfolioImportResult AccountNotFound(TimeSpan duration) =>
        new(PortfolioImportStatus.AccountNotFound, null, Array.Empty<AccountImportResult>(), duration);

    public static PortfolioImportResult PortfolioNotFound(TimeSpan duration) =>
        new(PortfolioImportStatus.PortfolioNotFound, null, Array.Empty<AccountImportResult>(), duration);

    public static PortfolioImportResult FileHasNoAccountInfo(string sourceSystem, TimeSpan duration) =>
        new(PortfolioImportStatus.FileHasNoAccountInfo, sourceSystem, Array.Empty<AccountImportResult>(), duration);

    public static PortfolioImportResult FileHasAccountInfo(string sourceSystem, TimeSpan duration) =>
        new(PortfolioImportStatus.FileHasAccountInfo, sourceSystem, Array.Empty<AccountImportResult>(), duration);
}
