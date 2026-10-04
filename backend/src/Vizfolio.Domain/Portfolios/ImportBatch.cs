namespace Vizfolio.Domain.Portfolios;

/// <summary>
/// One uploaded broker file: what was imported, from which file, by which parser, and (gzip) the file itself so it
/// can be re-parsed when a parser improves (<c>POST /api/imports/reprocess</c>) or replayed when an earlier import is
/// undone. Rows and snapshots the file inserted carry its id, which is what makes an import reversible.
/// </summary>
public sealed class ImportBatch
{
    private ImportBatch() { }

    public ImportBatch(
        Guid portfolioId,
        Guid? accountId,
        string fileName,
        string fileSha256,
        string parserSourceSystem,
        byte[]? content,
        string? contentType)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required.", nameof(portfolioId));
        if (string.IsNullOrWhiteSpace(fileSha256))
            throw new ArgumentException("File hash is required.", nameof(fileSha256));
        if (string.IsNullOrWhiteSpace(parserSourceSystem))
            throw new ArgumentException("Parser source system is required.", nameof(parserSourceSystem));

        ImportBatchId = Guid.NewGuid();
        PortfolioId = portfolioId;
        AccountId = accountId;
        FileName = string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName.Trim();
        FileSha256 = fileSha256.Trim().ToLowerInvariant();
        ParserSourceSystem = parserSourceSystem.Trim().ToUpperInvariant();
        Content = content;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();
        ImportedAt = DateTimeOffset.UtcNow;
        Status = ImportBatchStatus.Active;
    }

    public Guid ImportBatchId { get; private set; }

    public Guid PortfolioId { get; private set; }

    /// <summary>
    /// The account the file was uploaded to, for account-scoped imports (formats with no account details, such as
    /// the Vanguard report). Null for portfolio-scoped imports, which route each statement by its account number.
    /// </summary>
    public Guid? AccountId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the uploaded bytes; re-uploading the same file is recognised by it.</summary>
    public string FileSha256 { get; private set; } = string.Empty;

    public string ParserSourceSystem { get; private set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; private set; }

    /// <summary>When the stored file was last re-parsed (reprocess, or replay after an undo). Null if never.</summary>
    public DateTimeOffset? ReprocessedAt { get; private set; }

    /// <summary>The uploaded file, gzip-compressed. Null for files whose content wasn't kept.</summary>
    public byte[]? Content { get; private set; }

    public string? ContentType { get; private set; }

    /// <summary>The import's result (per-account counts and warnings) as JSON, shown again on a re-upload.</summary>
    public string? SummaryJson { get; private set; }

    public ImportBatchStatus Status { get; private set; }

    public DateTimeOffset? UndoneAt { get; private set; }

    public bool IsUndone => Status == ImportBatchStatus.Undone;

    public void RecordSummary(string summaryJson)
    {
        SummaryJson = string.IsNullOrWhiteSpace(summaryJson) ? null : summaryJson;
    }

    public void MarkReprocessed()
    {
        ReprocessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkUndone()
    {
        if (IsUndone)
            throw new InvalidOperationException("The import has already been undone.");

        Status = ImportBatchStatus.Undone;
        UndoneAt = DateTimeOffset.UtcNow;
    }
}

public enum ImportBatchStatus
{
    Active,
    Undone,
}
