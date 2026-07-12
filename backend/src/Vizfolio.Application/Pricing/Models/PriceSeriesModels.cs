namespace Vizfolio.Application.Pricing.Models;

/// <summary>A request to a price source for one symbol's daily closes over a date range.</summary>
public sealed record PriceSeriesRequest(string Symbol, string? Exchange, DateOnly From, DateOnly To);

/// <summary>One day's <b>raw / unadjusted</b> closing price.</summary>
public sealed record PricePoint(DateOnly AsOf, decimal Close);

/// <summary>A split event: shares held before <see cref="ExDate"/> are multiplied by Numerator/Denominator.</summary>
public sealed record SplitEvent(DateOnly ExDate, decimal Numerator, decimal Denominator);

/// <summary>What a price source returns for a series request.</summary>
public sealed record PriceSeriesResult(
    IReadOnlyList<PricePoint> Prices,
    IReadOnlyList<SplitEvent> Splits,
    string? CurrencyCode)
{
    public static PriceSeriesResult Empty { get; } =
        new(Array.Empty<PricePoint>(), Array.Empty<SplitEvent>(), null);
}

/// <summary>Options for a price-history import run.</summary>
public sealed record PriceHistoryImportOptions(
    IReadOnlyList<string>? Tickers = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool Force = false);
