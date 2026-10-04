namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>One day's raw (as-traded) close.</summary>
public readonly record struct PricePointData(DateOnly AsOf, decimal Close);

/// <summary>
/// A holding's daily closes, ascending, with on-or-before lookup by binary search. Carries the fixed price of a
/// stable-NAV (money market) fund, which values it whatever the price data says (see <see cref="StablePrices"/>).
/// </summary>
public sealed class PriceSeries
{
    private readonly PricePointData[] _ascending;

    public PriceSeries(IEnumerable<PricePointData> points, decimal? stablePrice = null)
    {
        _ascending = points.OrderBy(p => p.AsOf).ToArray();
        StablePrice = stablePrice;
    }

    public static PriceSeries Empty { get; } = new([]);

    /// <summary>The fund's fixed price per share when it's a stable-NAV fund; otherwise null.</summary>
    public decimal? StablePrice { get; }

    public int Count => _ascending.Length;

    public IReadOnlyList<PricePointData> Points => _ascending;

    /// <summary>The latest close on or before <paramref name="date"/>, whatever its age.</summary>
    public PricePointData? OnOrBefore(DateOnly date)
    {
        int lo = 0, hi = _ascending.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_ascending[mid].AsOf <= date) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        return found < 0 ? null : _ascending[found];
    }

    /// <summary>The latest close on or before the date, if it is no more than <paramref name="maxAgeDays"/> old.</summary>
    public PricePointData? Recent(DateOnly date, int maxAgeDays)
        => OnOrBefore(date) is { } p && date.DayNumber - p.AsOf.DayNumber <= maxAgeDays ? p : null;
}
