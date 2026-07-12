namespace Vizfolio.Application.Portfolios;

public enum HoldingValuationStatus
{
    /// <summary>Value is known (from price × quantity, or a broker snapshot).</summary>
    Covered,

    /// <summary>The holding was not held on the date — a true $0, counted as complete.</summary>
    NotHeld,

    /// <summary>The holding was held but has no usable valuation — an honest "unknown".</summary>
    Missing
}

public readonly record struct HoldingValuation(decimal Value, HoldingValuationStatus Status, DateOnly? SnapshotAsOf);

/// <summary>
/// Resolves a single holding's market value at a date using the fallback order in
/// docs/price-history-valuation.md §4/§8:
/// <list type="number">
///   <item>held (ledger quantity ≠ 0) and a price exists → <c>quantity × price</c>;</item>
///   <item>a valued broker snapshot at/before the date → the snapshot's market value;</item>
///   <item>not held (no ledger position and no broker position) → <c>$0</c>, complete;</item>
///   <item>held but neither priced nor snapshotted → missing (honest null downstream).</item>
/// </list>
/// Prices are on the raw/as-traded basis, matching the ledger quantities the roll-forward produces.
/// </summary>
public sealed class HoldingValuationResolver
{
    private readonly IReadOnlyDictionary<Guid, HoldingValuationData> _byHolding;
    private readonly Action<Guid, DateOnly>? _onUnresolvedSplit;

    public HoldingValuationResolver(
        IReadOnlyDictionary<Guid, HoldingValuationData> byHolding,
        Action<Guid, DateOnly>? onUnresolvedSplit = null)
    {
        _byHolding = byHolding;
        _onUnresolvedSplit = onUnresolvedSplit;
    }

    public HoldingValuation Resolve(Guid holdingId, DateOnly date)
    {
        if (!_byHolding.TryGetValue(holdingId, out var data))
            return new HoldingValuation(0m, HoldingValuationStatus.NotHeld, null);

        var quantity = HoldingQuantityCalculator.QuantityAt(
            data.Ledger,
            data.SplitFactors,
            date,
            exDate => _onUnresolvedSplit?.Invoke(holdingId, exDate));

        // (1) Preferred: value a held position from the price series.
        if (quantity != 0m)
        {
            var price = PriceOnOrBefore(data.PricesDescending, date);
            if (price.HasValue)
                return new HoldingValuation(quantity * price.Value, HoldingValuationStatus.Covered, null);
        }

        // (2) Fallback: the broker's ground-truth snapshot value.
        var valuedSnapshot = LatestSnapshot(data.SnapshotsDescending, date, requireValue: true);
        if (valuedSnapshot is { } vs)
            return new HoldingValuation(vs.MarketValue!.Value, HoldingValuationStatus.Covered, vs.AsOf);

        // (3)/(4): distinguish "not held → $0 complete" from "held but unknown → missing".
        var anySnapshot = LatestSnapshot(data.SnapshotsDescending, date, requireValue: false);
        var held = quantity != 0m || (anySnapshot is { } sn && sn.Quantity != 0m);
        return held
            ? new HoldingValuation(0m, HoldingValuationStatus.Missing, null)
            : new HoldingValuation(0m, HoldingValuationStatus.NotHeld, null);
    }

    private static decimal? PriceOnOrBefore(IReadOnlyList<PricePointData> pricesDescending, DateOnly date)
    {
        foreach (var p in pricesDescending)
            if (p.AsOf <= date) return p.Close;
        return null;
    }

    private static SnapshotData? LatestSnapshot(
        IReadOnlyList<SnapshotData> snapshotsDescending, DateOnly date, bool requireValue)
    {
        foreach (var s in snapshotsDescending)
        {
            if (s.AsOf > date) continue;
            if (requireValue && !s.MarketValue.HasValue) continue;
            return s;
        }

        return null;
    }
}

/// <summary>All per-holding data the resolver needs, pre-loaded so resolution does no I/O.</summary>
public sealed class HoldingValuationData
{
    public required IReadOnlyList<LedgerShareEntry> Ledger { get; init; }

    public required IReadOnlyDictionary<DateOnly, decimal> SplitFactors { get; init; }

    /// <summary>Price points sorted by <c>AsOf</c> descending (for on-or-before lookup).</summary>
    public required IReadOnlyList<PricePointData> PricesDescending { get; init; }

    /// <summary>Snapshots sorted by <c>AsOf</c> descending.</summary>
    public required IReadOnlyList<SnapshotData> SnapshotsDescending { get; init; }
}

public readonly record struct PricePointData(DateOnly AsOf, decimal Close);

public readonly record struct SnapshotData(DateOnly AsOf, decimal Quantity, decimal? MarketValue);
