namespace Vizfolio.Application.Extracts.Models;

public sealed record SecuritiesImportOptions(
    IReadOnlyList<string>? Tickers = null,
    bool Force = false);

public sealed record FundsImportOptions(
    IReadOnlyList<string>? SeriesIds = null,
    bool Force = false);

public sealed record ImportResult(
    int Considered,
    int Upserted,
    int Skipped,
    int Failed,
    IReadOnlyList<ImportFailure> Failures,
    IReadOnlyList<DataCleaningEntry> DataCleaning,
    TimeSpan Duration)
{
    public static ImportResult Empty(TimeSpan duration) =>
        new(0, 0, 0, 0, Array.Empty<ImportFailure>(), Array.Empty<DataCleaningEntry>(), duration);
}

public sealed record ImportFailure(string Key, string Reason);

/// <summary>
/// Records a value that the importer rewrote during ingestion (e.g. an unknown
/// reference code that was nulled out so the row could still be persisted).
/// Surface this back so it can be cleaned upstream.
/// </summary>
public sealed record DataCleaningEntry(string Field, string OriginalValue, string Reason, int Occurrences);
