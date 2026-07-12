namespace Vizfolio.Application.PortfolioImports.Models;

public enum PortfolioImportStatus
{
    Success,
    UnsupportedFormat,
    UnknownParser,
    AccountNotFound,
    PortfolioNotFound,
    FileHasNoAccountInfo,
    FileHasAccountInfo,
}

public sealed record PortfolioImportFailure(string Key, string Reason);

public sealed record PortfolioImportResult(
    PortfolioImportStatus Status,
    string? SourceSystem,
    IReadOnlyList<AccountImportResult> Accounts,
    TimeSpan Duration)
{
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
