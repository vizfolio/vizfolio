namespace Vizfolio.Domain.Pricing;

/// <summary>
/// A single day's closing price for a shared price series (a <c>Security</c>, or a bare symbol when
/// no Security is linked). Prices are stored on the <b>raw / as-traded basis</b> — the same basis as
/// the ledger's quantities — so that <c>marketValue = quantity(from ledger) × Close</c> is correct
/// across corporate actions. <see cref="Adjusted"/> is always <c>false</c> today; it documents the
/// invariant and leaves room for a future split-adjusted series to coexist.
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
        PriceSource source)
    {
        PriceHistoryId = Guid.NewGuid();
        Kind = kind;
        SecurityId = securityId;
        SymbolKey = symbolKey;
        AsOf = asOf;
        Close = close;
        CurrencyCode = NormalizeCurrency(currencyCode);
        Source = source;
        Adjusted = false;
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

    /// <summary>Always <c>false</c>: prices are stored on the raw/as-traded basis to match the ledger.</summary>
    public bool Adjusted { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public static PriceHistory ForSecurity(
        Guid securityId, DateOnly asOf, decimal close, string? currencyCode, PriceSource source)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));

        return new PriceHistory(
            PriceSeriesKind.Security, securityId, null, asOf, close, currencyCode, source);
    }

    public static PriceHistory ForSymbol(
        string symbolKey, DateOnly asOf, decimal close, string? currencyCode, PriceSource source)
    {
        var normalized = NormalizeSymbol(symbolKey);
        if (normalized is null)
            throw new ArgumentException("Symbol key is required.", nameof(symbolKey));

        return new PriceHistory(
            PriceSeriesKind.Symbol, null, normalized, asOf, close, currencyCode, source);
    }

    internal static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private static string? NormalizeCurrency(string? currencyCode) =>
        string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
}
