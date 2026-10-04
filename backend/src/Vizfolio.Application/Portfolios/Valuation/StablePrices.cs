namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// The fixed price per share of a stable-NAV (money market) fund — usually $1.00, though some funds and ETFs use $10
/// or $100. A held position in one can be valued even where its price history has no close, e.g. a Treasury money
/// market fund bought years before the price provider's coverage begins.
/// <para>
/// The authority is the fund's own SEC Form N-MFP, imported as <see cref="Domain.Funds.MoneyMarketFund"/> reference
/// data. For a ticker it doesn't cover (e.g. a renamed or retired fund), a price history that never moves is taken
/// as the stable price once there are enough observations to rule out coincidence.
/// </para>
/// </summary>
public static class StablePrices
{
    /// <summary>Fewest stored closes before an unchanging series is trusted to be a stable price.</summary>
    public const int MinObservations = 20;

    private const decimal RelativeTolerance = 0.0001m;

    /// <param name="registryPrice">The stable price from the money market registry, when the ticker is in it.</param>
    /// <param name="prices">The holding's stored closes.</param>
    public static decimal? Resolve(decimal? registryPrice, IReadOnlyCollection<PricePointData> prices)
    {
        if (registryPrice is > 0m) return registryPrice;
        if (prices.Count < MinObservations) return null;

        var first = prices.First().Close;
        if (first <= 0m) return null;
        return prices.All(p => Math.Abs(p.Close - first) <= first * RelativeTolerance) ? first : null;
    }
}
