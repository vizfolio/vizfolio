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
///   <item>a valued broker snapshot with no later trade and no newer price → the snapshot's market value;</item>
///   <item>held (snapshot-anchored ledger quantity &gt; 0) and a price exists → <c>quantity × price</c>;</item>
///   <item>a valued broker snapshot at/before the date → its market value, revalued at its per-share price
///   when shares changed since it (so a position sold down to zero is $0);</item>
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

        var anySnapshot = LatestSnapshot(data.SnapshotsDescending, date, requireValue: false);
        var valuedSnapshot = LatestSnapshot(data.SnapshotsDescending, date, requireValue: true);
        var quantity = QuantityAt(holdingId, data, anySnapshot, date);
        var price = PricePointOnOrBefore(data.PricesDescending, date);

        // (0) The broker's snapshot is ground truth when nothing newer is known: no share-affecting trade
        // since it, and no price newer than it. This keeps performance in step with the Holdings view.
        if (valuedSnapshot is { } fresh
            && !HoldingQuantityCalculator.HasActivityBetween(data.Ledger, fresh.AsOf, date)
            && (price is null || price.Value.AsOf <= fresh.AsOf))
            return new HoldingValuation(fresh.MarketValue!.Value, HoldingValuationStatus.Covered, fresh.AsOf);

        // (1) Preferred: value a held position from the price series. A negative quantity means the ledger
        // is inconsistent (e.g. sells/transfers-out whose acquisitions were never imported), so it is not
        // trusted for pricing and falls through to the snapshot.
        if (quantity > 0m && price is { } p)
            return new HoldingValuation(quantity * p.Close, HoldingValuationStatus.Covered, null);

        // (2) Fallback: no market price, so carry the broker's last snapshot forward. If shares changed since
        // it, its value is stale: revalue the current position at the snapshot's per-share price, so a
        // position sold down to zero is worth $0 rather than its old value.
        if (valuedSnapshot is { } vs)
        {
            var changedSince = HoldingQuantityCalculator.HasActivityBetween(data.Ledger, vs.AsOf, date);
            if (changedSince && quantity >= 0m && vs.Quantity != 0m)
            {
                var impliedUnitPrice = vs.MarketValue!.Value / vs.Quantity;
                return quantity == 0m
                    ? new HoldingValuation(0m, HoldingValuationStatus.NotHeld, null)
                    : new HoldingValuation(quantity * impliedUnitPrice, HoldingValuationStatus.Covered, vs.AsOf);
            }

            return new HoldingValuation(vs.MarketValue!.Value, HoldingValuationStatus.Covered, vs.AsOf);
        }

        // (3)/(4): distinguish "not held → $0 complete" from "held but unknown → missing".
        var held = quantity != 0m || (anySnapshot is { } sn && sn.Quantity != 0m);
        return held
            ? new HoldingValuation(0m, HoldingValuationStatus.Missing, null)
            : new HoldingValuation(0m, HoldingValuationStatus.NotHeld, null);
    }

    /// <summary>
    /// The share quantity held on <paramref name="date"/>. When a broker snapshot exists at/before the date its
    /// quantity is the anchor and only later ledger rows are rolled on top, so gaps in early imported history
    /// can't skew the position. A value-only snapshot (quantity 0 but a positive market value) carries no
    /// usable quantity, so the whole ledger is rolled forward from zero instead.
    /// </summary>
    private decimal QuantityAt(Guid holdingId, HoldingValuationData data, SnapshotData? anchor, DateOnly date)
    {
        Action<DateOnly> onUnresolved = exDate => _onUnresolvedSplit?.Invoke(holdingId, exDate);
        var hasUsableQuantity = anchor is { } a && !(a.Quantity == 0m && a.MarketValue is > 0m);

        return hasUsableQuantity
            ? HoldingQuantityCalculator.RollForward(
                anchor!.Value.Quantity, anchor.Value.AsOf, data.Ledger, data.SplitFactors, date, onUnresolved)
            : HoldingQuantityCalculator.QuantityAt(data.Ledger, data.SplitFactors, date, onUnresolved);
    }

    private static PricePointData? PricePointOnOrBefore(IReadOnlyList<PricePointData> pricesDescending, DateOnly date)
    {
        foreach (var p in pricesDescending)
            if (p.AsOf <= date) return p;
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
