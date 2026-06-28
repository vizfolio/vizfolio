namespace Vizfolio.Application.PortfolioImports.Models;

public enum PortfolioImportStatus
{
    Success,
    UnsupportedFormat,
    AccountNotFound
}

public sealed record PortfolioImportFailure(string Key, string Reason);

public sealed record PortfolioImportResult(
    PortfolioImportStatus Status,
    string? SourceSystem,
    string? SourceInstitution,
    string? SourceAccountNumber,
    int Considered,
    int Inserted,
    int Skipped,
    int Failed,
    IReadOnlyList<PortfolioImportFailure> Failures,
    TimeSpan Duration)
{
    public static PortfolioImportResult UnsupportedFormat(TimeSpan duration) =>
        new(PortfolioImportStatus.UnsupportedFormat, null, null, null, 0, 0, 0, 0,
            Array.Empty<PortfolioImportFailure>(), duration);

    public static PortfolioImportResult AccountNotFound(TimeSpan duration) =>
        new(PortfolioImportStatus.AccountNotFound, null, null, null, 0, 0, 0, 0,
            Array.Empty<PortfolioImportFailure>(), duration);
}
