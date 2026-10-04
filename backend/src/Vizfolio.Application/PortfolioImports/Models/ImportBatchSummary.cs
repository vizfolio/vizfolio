namespace Vizfolio.Application.PortfolioImports.Models;

/// <summary>What an import did, stored as JSON on its ImportBatch and shown again when the same file is re-uploaded.</summary>
public sealed record ImportBatchSummary(
    string SourceSystem,
    IReadOnlyList<AccountImportResult> Accounts,
    IReadOnlyList<ImportWarning> Warnings);

/// <summary>Result of re-parsing stored files with the current parsers (POST /api/imports/reprocess).</summary>
public sealed record ReprocessResult(
    int Batches,
    int Inserted,
    int Updated,
    int SnapshotsInserted,
    IReadOnlyList<ReprocessedBatch> PerBatch);

/// <summary>One stored file's reprocess outcome. <see cref="Skipped"/> says why it wasn't (e.g. its parser is gone).</summary>
public sealed record ReprocessedBatch(
    Guid ImportBatchId,
    string FileName,
    int Inserted,
    int Updated,
    int SnapshotsInserted,
    string? Skipped);
