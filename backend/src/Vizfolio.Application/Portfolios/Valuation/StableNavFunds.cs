namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// Recognises stable-NAV (money-market) funds, which are worth $1.00 a share by design. A held position in one
/// can be valued even before its price history starts — e.g. a Treasury money-market fund bought years before
/// the price provider's coverage begins.
/// </summary>
public static class StableNavFunds
{
    /// <summary>Fewest stored closes before an all-$1.00 series is trusted to be a stable-NAV fund.</summary>
    public const int MinObservations = 20;

    private const decimal Tolerance = 0.0001m;

    /// <summary>Common brokerage money-market and settlement funds.</summary>
    public static readonly IReadOnlySet<string> KnownTickers = new HashSet<string>(StringComparer.Ordinal)
    {
        // Vanguard
        "VMFXX", "VMMXX", "VUSXX", "VMRXX",
        // Fidelity
        "SPAXX", "FDRXX", "FZFXX", "SPRXX", "FZDXX", "FTEXX",
        // Schwab
        "SWVXX", "SNVXX", "SNOXX", "SNSXX", "SWTXX",
    };

    /// <summary>
    /// True when the symbol is a known money-market fund, or its price history is consistently $1.00 over
    /// enough observations to rule out coincidence.
    /// </summary>
    public static bool IsStableNav(string? symbol, IReadOnlyCollection<PricePointData> prices)
    {
        if (symbol is not null && KnownTickers.Contains(symbol.Trim().ToUpperInvariant())) return true;
        return prices.Count >= MinObservations && prices.All(p => Math.Abs(p.Close - 1m) <= Tolerance);
    }
}
