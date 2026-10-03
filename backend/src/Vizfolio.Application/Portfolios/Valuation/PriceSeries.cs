namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>One day's raw (as-traded) close.</summary>
public readonly record struct PricePointData(DateOnly AsOf, decimal Close);

/// <summary>
/// A holding's daily closes, ascending, with on-or-before lookup by binary search. Knows whether the series is a
/// stable-NAV (money-market) fund, worth $1.00 a share whatever the price data says.
/// </summary>
public sealed class PriceSeries
{
    private readonly PricePointData[] _ascending;

    public PriceSeries(IEnumerable<PricePointData> points, bool isStableNav)
    {
        _ascending = points.OrderBy(p => p.AsOf).ToArray();
        IsStableNav = isStableNav;
    }

    public static PriceSeries Empty { get; } = new([], isStableNav: false);

    public bool IsStableNav { get; }

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
