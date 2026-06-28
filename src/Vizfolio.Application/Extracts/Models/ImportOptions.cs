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
    TimeSpan Duration)
{
    public static ImportResult Empty(TimeSpan duration) =>
        new(0, 0, 0, 0, Array.Empty<ImportFailure>(), duration);
}

public sealed record ImportFailure(string Key, string Reason);
