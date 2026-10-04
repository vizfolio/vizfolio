namespace Vizfolio.Domain.Portfolios;

/// <summary>
/// An already-stored transaction that an import re-mapped (its parser now normalizes the broker's row differently —
/// e.g. after a label-mapping fix), with the values before and after. Undoing the import restores the previous
/// values, but only while the row still holds the ones this import set (a later import may have changed it again).
/// </summary>
public sealed class ImportBatchRowUpdate
{
    private ImportBatchRowUpdate() { }

    public ImportBatchRowUpdate(Guid importBatchId, AccountTransaction row, ImportedTransactionFields next)
    {
        if (importBatchId == Guid.Empty)
            throw new ArgumentException("Import batch ID is required.", nameof(importBatchId));
        ArgumentNullException.ThrowIfNull(row);

        ImportBatchRowUpdateId = Guid.NewGuid();
        ImportBatchId = importBatchId;
        AccountTransactionId = row.AccountTransactionId;
        Previous = ImportedTransactionFields.Of(row);
        Next = next;
        RecordedAt = DateTimeOffset.UtcNow;
    }

    public Guid ImportBatchRowUpdateId { get; private set; }

    public Guid ImportBatchId { get; private set; }

    public Guid AccountTransactionId { get; private set; }

    public ImportedTransactionFields Previous { get; private set; } = null!;

    public ImportedTransactionFields Next { get; private set; } = null!;

    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>
/// The fields of a transaction that only the import pipeline writes (Appendix A.11 of the accuracy roadmap): a
/// re-import or reprocess brings them in line with the current parser. User corrections will live in a separate
/// overlay, never here.
/// </summary>
public sealed record ImportedTransactionFields(
    TransactionType Type,
    decimal Amount,
    decimal? Quantity,
    decimal? Price,
    DateOnly? SettlementDate,
    string? SourceType,
    bool IsSettlementFund)
{
    public static ImportedTransactionFields Of(AccountTransaction row) =>
        new(row.Type, row.Amount, row.Quantity, row.Price, row.SettlementDate, row.SourceType, row.IsSettlementFund);
}
