namespace Vizfolio.Domain.Pricing;

/// <summary>
/// A single day's closing price for a shared price series (a <c>Security</c>, or a bare symbol when
/// no Security is linked). Prices are stored on the <b>raw / as-traded basis</b> — the same basis as
/// the ledger's quantities — so that <c>marketValue = quantity(from ledger) × Close</c> is correct
/// across corporate actions. A source that only offers split/dividend-<see cref="Adjusted"/> closes (Stooq) is stored
/// with the flag set, and valuation never uses those rows.
/// </summary>
public sealed class PriceHistory
{
    private PriceHistory() { }

    private PriceHistory(
        PriceSeriesKind kind,
        Guid? securityId,
        string? symbolKey,
        DateOnly asOf,
        decimal close,
        string? currencyCode,
        PriceSource source,
        bool adjusted)
    {
        PriceHistoryId = Guid.NewGuid();
        Kind = kind;
        SecurityId = securityId;
        SymbolKey = symbolKey;
        AsOf = asOf;
        Close = close;
        CurrencyCode = NormalizeCurrency(currencyCode);
        Source = source;
        Adjusted = adjusted;
        FetchedAt = DateTimeOffset.UtcNow;
    }

    public Guid PriceHistoryId { get; private set; }

    public PriceSeriesKind Kind { get; private set; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="PriceSeriesKind.Security"/>.</summary>
    public Guid? SecurityId { get; private set; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="PriceSeriesKind.Symbol"/>. Uppercased.</summary>
    public string? SymbolKey { get; private set; }

    public DateOnly AsOf { get; private set; }

    public decimal Close { get; private set; }

    public string? CurrencyCode { get; private set; }

    public PriceSource Source { get; private set; }

    /// <summary>
    /// True for split/dividend-adjusted closes. Valuation only uses raw (as-traded) closes, which match the ledger's
    /// quantities; adjusted ones are kept for reference.
    /// </summary>
    public bool Adjusted { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public static PriceHistory ForSecurity(
        Guid securityId, DateOnly asOf, decimal close, string? currencyCode, PriceSource source, bool adjusted = false)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));

        return new PriceHistory(
            PriceSeriesKind.Security, securityId, null, asOf, close, currencyCode, source, adjusted);
    }

    public static PriceHistory ForSymbol(
        string symbolKey, DateOnly asOf, decimal close, string? currencyCode, PriceSource source, bool adjusted = false)
    {
        var normalized = NormalizeSymbol(symbolKey);
        if (normalized is null)
            throw new ArgumentException("Symbol key is required.", nameof(symbolKey));

        return new PriceHistory(
            PriceSeriesKind.Symbol, null, normalized, asOf, close, currencyCode, source, adjusted);
    }

    internal static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private static string? NormalizeCurrency(string? currencyCode) =>
        string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
}
