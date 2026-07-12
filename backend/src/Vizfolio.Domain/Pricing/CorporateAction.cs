namespace Vizfolio.Domain.Pricing;

/// <summary>
/// A sparse corporate-action event for a price series (kept separate from the dense daily
/// <see cref="PriceHistory"/> rows). A <see cref="CorporateActionType.Split"/> multiplies the running
/// ledger quantity by <see cref="SplitNumerator"/>/<see cref="SplitDenominator"/> for shares held on
/// or before <see cref="ExDate"/>. This table is the <b>authoritative</b> source of split factors:
/// a <c>Split</c> ledger transaction is only applied to the roll-forward when a matching action exists
/// here, which avoids double-counting when a broker already reports post-split units on later trades.
/// </summary>
public sealed class CorporateAction
{
    private CorporateAction() { }

    private CorporateAction(
        PriceSeriesKind kind,
        Guid? securityId,
        string? symbolKey,
        CorporateActionType type,
        DateOnly exDate,
        decimal splitNumerator,
        decimal splitDenominator,
        PriceSource source)
    {
        if (splitNumerator <= 0m)
            throw new ArgumentOutOfRangeException(nameof(splitNumerator), "Split numerator must be positive.");
        if (splitDenominator <= 0m)
            throw new ArgumentOutOfRangeException(nameof(splitDenominator), "Split denominator must be positive.");

        CorporateActionId = Guid.NewGuid();
        Kind = kind;
        SecurityId = securityId;
        SymbolKey = symbolKey;
        Type = type;
        ExDate = exDate;
        SplitNumerator = splitNumerator;
        SplitDenominator = splitDenominator;
        Source = source;
        FetchedAt = DateTimeOffset.UtcNow;
    }

    public Guid CorporateActionId { get; private set; }

    public PriceSeriesKind Kind { get; private set; }

    public Guid? SecurityId { get; private set; }

    public string? SymbolKey { get; private set; }

    public CorporateActionType Type { get; private set; }

    public DateOnly ExDate { get; private set; }

    public decimal SplitNumerator { get; private set; }

    public decimal SplitDenominator { get; private set; }

    public PriceSource Source { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    /// <summary>The multiplier applied to a quantity held before the ex-date (e.g. 4-for-1 → 4).</summary>
    public decimal SplitFactor => SplitNumerator / SplitDenominator;

    public static CorporateAction SplitForSecurity(
        Guid securityId, DateOnly exDate, decimal numerator, decimal denominator, PriceSource source)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security ID is required.", nameof(securityId));

        return new CorporateAction(
            PriceSeriesKind.Security, securityId, null,
            CorporateActionType.Split, exDate, numerator, denominator, source);
    }

    public static CorporateAction SplitForSymbol(
        string symbolKey, DateOnly exDate, decimal numerator, decimal denominator, PriceSource source)
    {
        var normalized = PriceHistory.NormalizeSymbol(symbolKey);
        if (normalized is null)
            throw new ArgumentException("Symbol key is required.", nameof(symbolKey));

        return new CorporateAction(
            PriceSeriesKind.Symbol, null, normalized,
            CorporateActionType.Split, exDate, numerator, denominator, source);
    }
}
