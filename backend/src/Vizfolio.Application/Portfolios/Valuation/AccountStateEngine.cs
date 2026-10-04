using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// Values one account on any date — every holding's shares and the account's cash — from its ledger, the broker's
/// statements, price history and splits. The single source of valuation for performance, the Holdings view and
/// implied contributions (docs/price-history-valuation.md §11).
/// <para><b>Shares</b> roll forward from known positions (anchors): stored snapshots, or a <b>derived opening</b> —
/// the earliest broker position rolled <i>back</i> to the day before the account's first transaction, so a partial
/// history (e.g. an 18-month QFX) starts from the positions the account really held, with no manual opening
/// balance. Provider splits apply at the start of their ex-date; a broker row reporting the same split is not
/// applied twice, and a broker split the provider doesn't know applies its own ratio (or, with no ratio, its change
/// in shares). At each later anchor the rolled quantity is compared with the broker's and then reset to it.</para>
/// <para><b>Cash</b> is uninvested cash plus the settlement fund (<see cref="AccountCash"/>), anchored on the
/// settlement fund's and any cash holding's snapshots the same way.</para>
/// <para>A ledger/broker disagreement worth more than <see cref="ValuationOptions.MismatchMaterialityMin"/> or
/// <see cref="ValuationOptions.MismatchMaterialityPct"/> of the account makes the component <i>missing</i> between
/// the previous anchor and that statement — an honest null rather than a guess.</para>
/// </summary>
public sealed class AccountStateEngine
{
    private const decimal ZeroQuantity = 0.001m;
    private const decimal ZeroCash = 0.005m;
    private const decimal ImmaterialValue = 1m;
    private const int SplitMatchDays = 10;

    private readonly ValuationOptions _options;
    private readonly IReadOnlySet<string> _settlement;
    private readonly List<ShareComponent> _shares = [];
    private readonly CashComponent _cash;
    private readonly List<LedgerRow> _flowRows;
    private readonly Dictionary<Guid, ShareComponent> _shareByHolding = new();
    private readonly List<ReconciliationFinding> _findings = [];

    public AccountStateEngine(AccountValuationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        AccountId = input.AccountId;
        _options = input.Options;

        var ledger = input.Ledger;
        _settlement = SettlementFunds.Identify(ledger, input.Holdings);
        OpeningDate = ledger.Count == 0 ? null : ledger.Min(r => r.TradeDate).AddDays(-1);

        var rowsByHolding = ledger
            .Where(r => r.HoldingId is not null)
            .GroupBy(r => r.HoldingId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cashHoldings = new List<HoldingInput>();
        foreach (var holding in input.Holdings)
        {
            if (holding.Kind == AccountHoldingKind.Cash || SettlementFunds.IsSettlement(holding.Symbol, _settlement))
            {
                cashHoldings.Add(holding);
                continue;
            }

            var component = new ShareComponent(
                holding, rowsByHolding.TryGetValue(holding.HoldingId, out var rows) ? rows : []);
            BuildShareTimeline(component);
            _shares.Add(component);
            _shareByHolding[holding.HoldingId] = component;
        }

        var anyPreHistory = _shares.Any(s => s.Opening.Class == OpeningClass.PreHistory);
        _cash = BuildCash(ledger, cashHoldings, anyPreHistory);

        _flowRows = ledger
            .Where(r => r.Type is TransactionType.Deposit or TransactionType.Withdrawal or TransactionType.Transfer)
            .OrderBy(r => r.TradeDate)
            .ToList();

        ApplyMateriality();
        RecordStructuralFindings();
    }

    public Guid AccountId { get; }

    /// <summary>The day before the account's first transaction — where derived openings sit. Null with no ledger.</summary>
    public DateOnly? OpeningDate { get; }

    public IReadOnlySet<string> SettlementTickers => _settlement;

    /// <summary>Each holding's position (and cash, with a null id) at <see cref="OpeningDate"/>.</summary>
    public IReadOnlyList<DerivedOpening> Openings
        => _shares.Select(s => s.Opening).Append(_cash.Opening).ToList();

    /// <summary>
    /// Opening cash to seed the implied-contribution roll with: the derived pre-history cash when the history is
    /// partial (some position predates it), else zero — see roadmap Appendix A.2.
    /// </summary>
    public decimal OpeningCashSeed => _cash.Opening.Class == OpeningClass.PreHistory ? _cash.Opening.Quantity : 0m;

    public IReadOnlyList<ReconciliationFinding> Findings => _findings;

    /// <summary>The account's value at the close of <paramref name="date"/>.</summary>
    public AccountValue ValueAt(DateOnly date) => ValueAt(date, applyWindows: true);

    /// <summary>
    /// Money (and shares, valued) crossing the account boundary in <c>[from, to]</c>: deposits, withdrawals, cash
    /// transfers, implied contributions, and in-kind transfers valued at the day's price.
    /// </summary>
    public IReadOnlyList<ExternalFlow> FlowsBetween(DateOnly from, DateOnly to)
    {
        var flows = new List<ExternalFlow>();
        foreach (var row in _flowRows)
        {
            if (row.TradeDate < from || row.TradeDate > to) continue;
            flows.Add(ToFlow(row));
        }

        return flows;
    }

    /// <summary>
    /// The stretches of <c>[from, to]</c> where a component couldn't be valued, one per component and cause and
    /// unbroken run of days — e.g. "no price for XYZ from 2019-07-01 to 2021-05-31". Scans every day (a few
    /// thousand cheap valuations for a long history), so it lives here rather than in each caller.
    /// </summary>
    public IReadOnlyList<MissingInterval> MissingIntervals(DateOnly from, DateOnly to)
    {
        var open = new Dictionary<(Guid?, MissingCause), MissingInterval>();
        var closed = new List<MissingInterval>();

        for (var date = from; date <= to; date = date.AddDays(1))
            foreach (var component in ValueAt(date).Components)
            {
                if (component.Status != HoldingValuationStatus.Missing || component.Cause is not { } cause) continue;

                var key = (component.HoldingId, cause);
                if (open.TryGetValue(key, out var run) && run.To == date.AddDays(-1))
                {
                    open[key] = run with { To = date };
                    continue;
                }

                if (run is not null) closed.Add(run);
                open[key] = new MissingInterval(component.HoldingId, component.Symbol, cause, date, date);
            }

        return closed.Concat(open.Values)
            .OrderBy(i => i.From)
            .ThenBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------------- shares ----------------

    private void BuildShareTimeline(ShareComponent c)
    {
        var (splits, handledSplitRows) = Splits(c);

        var anchors = c.Holding.AnchorsAscending
            .Where(a => !(a.Quantity == 0m && a.MarketValue is > 0m)) // value-only snapshot: no usable quantity
            .ToList();

        // Pass 1 with every row, to recognise broker rows that just record a split's extra shares.
        var deltas = ShareDeltas(c.Rows, skip: handledSplitRows);
        var provisional = Walk(deltas, splits, anchors, derived: null, recordMismatches: false);
        var skip = SplitShareRows(c.Rows, splits, provisional);
        if (skip.Count > 0) deltas = ShareDeltas(c.Rows, skip.Union(handledSplitRows).ToHashSet());

        c.Opening = DeriveOpening(c.Holding.HoldingId, c.Holding.Symbol, deltas, splits, anchors);
        var derived = OpeningDate is { } openingDate
                      && c.Opening.Class is OpeningClass.None or OpeningClass.PreHistory
                      && !HasAnchorOnOrBefore(anchors, openingDate)
            ? new PositionAnchor(openingDate, c.Opening.Quantity, null, AnchorSource.Derived)
            : (PositionAnchor?)null;

        c.Timeline = Walk(deltas, splits, anchors, derived, recordMismatches: true);
        c.ActivityDates = deltas.Select(d => d.Date).Concat(splits.Select(s => s.ExDate)).Distinct().Order().ToArray();
        c.ValuedAnchors = c.Holding.AnchorsAscending.Where(a => a.MarketValue.HasValue).ToList();
    }

    /// <summary>
    /// The holding's splits (roadmap Appendix A.7). Provider splits are authoritative, so a broker split row within
    /// <see cref="SplitMatchDays"/> of one is the same event and adds nothing. A broker split the provider doesn't know
    /// applies its own ratio at the start of its date; one without a ratio falls back to its change in shares (its
    /// quantity, via <see cref="ShareDeltas"/>). Either way it's reported as an unmatched split. Returns the split rows
    /// whose quantity must not also be added.
    /// </summary>
    private (List<SplitAction> Splits, HashSet<Guid> HandledRows) Splits(ShareComponent c)
    {
        var provider = c.Holding.Splits.Where(s => s.Factor > 0m && s.Factor != 1m).ToList();
        var splits = new List<SplitAction>(provider);
        var handled = new HashSet<Guid>();

        foreach (var row in c.Rows.Where(r => r.Type == TransactionType.Split))
        {
            if (provider.Any(s => Math.Abs(s.ExDate.DayNumber - row.TradeDate.DayNumber) <= SplitMatchDays))
            {
                handled.Add(row.TransactionId);
                continue;
            }

            _findings.Add(new ReconciliationFinding(
                FindingCode.UnmatchedSplit, c.Holding.HoldingId, c.Holding.Symbol, row.TradeDate, null, null, null, false));
            if (row.SplitFactor is { } factor && factor > 0m && factor != 1m)
            {
                splits.Add(new SplitAction(row.TradeDate, factor));
                handled.Add(row.TransactionId);
            }
        }

        return (splits.OrderBy(s => s.ExDate).ToList(), handled);
    }

    /// <summary>
    /// How each row moves the share count (sells always reduce; income/Other only with a quantity; a split row by its
    /// change in shares unless its split is applied as a ratio).
    /// </summary>
    private static List<(DateOnly Date, decimal Delta)> ShareDeltas(IEnumerable<LedgerRow> rows, HashSet<Guid>? skip)
    {
        var deltas = new List<(DateOnly, decimal)>();
        foreach (var row in rows)
        {
            if (skip?.Contains(row.TransactionId) == true) continue;
            var delta = row.Type switch
            {
                TransactionType.Buy or TransactionType.Reinvest or TransactionType.Transfer => row.Quantity ?? 0m,
                TransactionType.Dividend or TransactionType.CapitalGain or TransactionType.Other => row.Quantity ?? 0m,
                TransactionType.Split => row.Quantity ?? 0m,
                // Brokers disagree on the sign of sold units; a sell always reduces the position.
                TransactionType.Sell => -Math.Abs(row.Quantity ?? 0m),
                _ => 0m,
            };
            if (delta != 0m) deltas.Add((row.TradeDate, delta));
        }

        return deltas;
    }

    /// <summary>
    /// Broker rows that only record a provider split's extra shares (a no-cost share row within a few days of the
    /// split, for about <c>shares × (factor − 1)</c>) — applying both would double the split.
    /// </summary>
    private static HashSet<Guid> SplitShareRows(IEnumerable<LedgerRow> rows, List<SplitAction> splits, Timeline provisional)
    {
        var skip = new HashSet<Guid>();
        if (splits.Count == 0) return skip;

        foreach (var row in rows)
        {
            if (row.Type is not (TransactionType.Buy or TransactionType.Transfer or TransactionType.Other or TransactionType.Split)) continue;
            if (row.Quantity is not > 0m || Math.Abs(row.Amount) >= 0.01m) continue;

            foreach (var split in splits)
            {
                if (Math.Abs(split.ExDate.DayNumber - row.TradeDate.DayNumber) > SplitMatchDays) continue;
                var before = provisional.QuantityAt(split.ExDate.AddDays(-1)).Quantity
                             - (row.TradeDate < split.ExDate ? row.Quantity.Value : 0m);
                var expected = before * (split.Factor - 1m);
                if (expected > 0m && Math.Abs(row.Quantity.Value - expected) <= 0.01m * row.Quantity.Value)
                {
                    skip.Add(row.TransactionId);
                    break;
                }
            }
        }

        return skip;
    }

    private DerivedOpening DeriveOpening(
        Guid holdingId, string? symbol, List<(DateOnly Date, decimal Delta)> deltas, List<SplitAction> splits, List<PositionAnchor> anchors)
    {
        if (OpeningDate is not { } opening)
            return new DerivedOpening(holdingId, symbol, DateOnly.MinValue, 0m, OpeningClass.None);

        // A stored position on/before the opening (e.g. a user's opening balance) is authoritative.
        if (Latest(anchors, opening) is { } stored)
            return new DerivedOpening(holdingId, symbol, opening, stored.Quantity, ClassOf(stored.Quantity, stored.UnitPrice));

        if (anchors.Count == 0)
            return new DerivedOpening(holdingId, symbol, opening, 0m, OpeningClass.None, Verified: false);

        // Roll the earliest broker position back to the opening: undo each day's rows, then that day's split.
        var earliest = anchors[0];
        var q = earliest.Quantity;
        var byDate = deltas.Where(d => d.Date > opening && d.Date <= earliest.AsOf)
            .GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Sum(d => d.Delta));
        var splitDates = splits.Where(s => s.ExDate > opening && s.ExDate <= earliest.AsOf).ToDictionary(s => s.ExDate, s => s.Factor);
        foreach (var date in byDate.Keys.Concat(splitDates.Keys).Distinct().OrderDescending())
        {
            if (byDate.TryGetValue(date, out var delta)) q -= delta;
            if (splitDates.TryGetValue(date, out var factor)) q /= factor;
        }

        return new DerivedOpening(holdingId, symbol, opening, q, ClassOf(q, earliest.UnitPrice));
    }

    private static OpeningClass ClassOf(decimal quantity, decimal? unitPrice)
    {
        if (Math.Abs(quantity) < ZeroQuantity || (unitPrice is { } p && Math.Abs(quantity * p) < ImmaterialValue))
            return OpeningClass.None;
        return quantity > 0m ? OpeningClass.PreHistory : OpeningClass.Inconsistent;
    }

    private static bool HasAnchorOnOrBefore(List<PositionAnchor> anchors, DateOnly? date)
        => date is { } d && anchors.Any(a => a.AsOf <= d);

    private static bool HasStoredAnchorOnOrBefore(IReadOnlyList<PositionAnchor> anchors, DateOnly date)
        => anchors.Any(a => a.AsOf <= date && a.Source != AnchorSource.Derived);

    /// <summary>The latest anchor on or before the date (anchors ascending), or null.</summary>
    private static PositionAnchor? Latest(List<PositionAnchor> anchorsAscending, DateOnly date)
    {
        for (var i = anchorsAscending.Count - 1; i >= 0; i--)
            if (anchorsAscending[i].AsOf <= date) return anchorsAscending[i];
        return null;
    }

    // ---------------- cash ----------------

    private CashComponent BuildCash(IReadOnlyList<LedgerRow> ledger, List<HoldingInput> cashHoldings, bool anyPreHistory)
    {
        // Each date any cash holding has a snapshot is a cash anchor: the sum of their values that day.
        var anchors = cashHoldings
            .SelectMany(h => h.AnchorsAscending)
            .GroupBy(a => a.AsOf)
            .OrderBy(g => g.Key)
            .Select(g => new PositionAnchor(g.Key, g.Sum(a => a.MarketValue ?? a.Quantity), g.Sum(a => a.MarketValue ?? a.Quantity), g.First().Source))
            .ToList();

        var days = ledger
            .GroupBy(r => r.TradeDate)
            .Select(g => (Date: g.Key, Delta: AccountCash.DayEffect(g.ToList(), _settlement)))
            .Where(d => d.Delta != 0m)
            .ToList();

        var opening = DeriveCashOpening(ledger, anchors, anyPreHistory);
        var derived = opening.Class is OpeningClass.None or OpeningClass.PreHistory && OpeningDate is { } od
                      && !HasAnchorOnOrBefore(anchors, od) && anchors.Count > 0
            ? new PositionAnchor(od, opening.Quantity, opening.Quantity, AnchorSource.Derived)
            : (PositionAnchor?)null;

        var cash = new CashComponent { Opening = opening, StoredAnchorDates = anchors.Select(a => a.AsOf).ToList() };
        cash.Timeline = Walk(days, splits: [], anchors, derived, recordMismatches: true);
        return cash;
    }

    private DerivedOpening DeriveCashOpening(IReadOnlyList<LedgerRow> ledger, List<PositionAnchor> anchors, bool anyPreHistory)
    {
        if (OpeningDate is not { } opening)
            return new DerivedOpening(null, null, DateOnly.MinValue, 0m, OpeningClass.None);
        if (Latest(anchors, opening) is { } stored)
            return new DerivedOpening(null, null, opening, stored.Quantity, CashClass(stored.Quantity));
        if (anchors.Count == 0)
            return new DerivedOpening(null, null, opening, 0m, OpeningClass.None, Verified: false);
        if (!anyPreHistory)
            return new DerivedOpening(null, null, opening, 0m, OpeningClass.None);

        // Partial history: back the earliest broker cash out over the imported rows (not Vizfolio's implied ones).
        var earliest = anchors[0];
        var imported = ledger.Where(r => !r.IsImplied && r.TradeDate > opening && r.TradeDate <= earliest.AsOf)
            .GroupBy(r => r.TradeDate)
            .Sum(g => AccountCash.DayEffect(g.ToList(), _settlement));
        var cash = earliest.Quantity - imported;
        var cls = CashClass(cash);
        return new DerivedOpening(null, null, opening, cls == OpeningClass.PreHistory ? cash : 0m, cls);
    }

    private static OpeningClass CashClass(decimal cash)
        => Math.Abs(cash) < ImmaterialValue ? OpeningClass.None
            : cash > 0m ? OpeningClass.PreHistory
            : OpeningClass.Inconsistent;

    // ---------------- timelines & reconciliation ----------------

    /// <summary>
    /// Rolls a quantity (shares or cash) through its events, resetting to each anchor and noting how far the roll
    /// had drifted from it. Splits apply at the start of their ex-date; anchors are end-of-day positions.
    /// </summary>
    private Timeline Walk(
        List<(DateOnly Date, decimal Delta)> deltas,
        List<SplitAction> splits,
        List<PositionAnchor> anchors,
        PositionAnchor? derived,
        bool recordMismatches)
    {
        var allAnchors = derived is { } d ? anchors.Append(d).OrderBy(a => a.AsOf).ToList() : anchors;
        var deltaByDate = deltas.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.Delta));
        var splitByDate = splits.GroupBy(s => s.ExDate).ToDictionary(g => g.Key, g => g.Aggregate(1m, (f, s) => f * s.Factor));
        var anchorByDate = allAnchors.GroupBy(a => a.AsOf).ToDictionary(g => g.Key, g => g.Last());

        var timeline = new Timeline();
        var q = 0m;
        DateOnly? previousAnchor = null;
        foreach (var date in deltaByDate.Keys.Concat(splitByDate.Keys).Concat(anchorByDate.Keys).Distinct().Order())
        {
            if (splitByDate.TryGetValue(date, out var factor)) q *= factor;
            if (deltaByDate.TryGetValue(date, out var delta)) q += delta;

            if (anchorByDate.TryGetValue(date, out var anchor))
            {
                if (recordMismatches && anchor.Source != AnchorSource.Derived && Math.Abs(q - anchor.Quantity) >= ZeroQuantity)
                    timeline.Mismatches.Add(new Mismatch(date, previousAnchor, q, anchor.Quantity, anchor.UnitPrice));
                q = anchor.Quantity;
                previousAnchor = date;
            }

            timeline.Add(date, q);
        }

        return timeline;
    }

    /// <summary>
    /// Decides which ledger/broker drifts matter: worth more than the materiality threshold of the account's value
    /// at that statement. A material drift makes the component missing back to the previous anchor.
    /// </summary>
    private void ApplyMateriality()
    {
        var accountValueAt = new Dictionary<DateOnly, decimal>();
        decimal AccountValueAt(DateOnly date)
        {
            if (!accountValueAt.TryGetValue(date, out var v))
                accountValueAt[date] = v = Math.Abs(ValueAt(date, applyWindows: false).Value);
            return v;
        }

        foreach (var share in _shares)
            foreach (var m in share.Timeline.Mismatches)
            {
                var unitPrice = m.AnchorUnitPrice
                                ?? share.Holding.Prices.OnOrBefore(m.Date)?.Close
                                ?? share.Holding.Prices.StablePrice;
                var diff = m.Rolled - m.Anchor;
                var diffValue = unitPrice is { } p ? Math.Abs(diff * p) : (decimal?)null;
                var material = diffValue is { } dv
                    ? dv > Threshold(AccountValueAt(m.Date))
                    : Math.Abs(diff) > 0.005m * Math.Max(Math.Abs(m.Anchor), ZeroQuantity);
                _findings.Add(new ReconciliationFinding(
                    FindingCode.QuantityMismatch, share.Holding.HoldingId, share.Holding.Symbol, m.Date,
                    m.Rolled, m.Anchor, diffValue, material));
                if (material) share.Windows.Add((m.PreviousAnchor, m.Date));
            }

        foreach (var m in _cash.Timeline.Mismatches)
        {
            var diffValue = Math.Abs(m.Rolled - m.Anchor);
            var material = diffValue > Threshold(AccountValueAt(m.Date));
            _findings.Add(new ReconciliationFinding(
                FindingCode.CashMismatch, null, null, m.Date, m.Rolled, m.Anchor, diffValue, material));
            if (material) _cash.Windows.Add((m.PreviousAnchor, m.Date));
        }
    }

    private decimal Threshold(decimal accountValue)
        => Math.Max(_options.MismatchMaterialityMin, _options.MismatchMaterialityPct * accountValue);

    private void RecordStructuralFindings()
    {
        foreach (var share in _shares)
        {
            if (share.Opening.Class == OpeningClass.PreHistory)
                _findings.Add(new ReconciliationFinding(
                    FindingCode.PreHistoryPosition, share.Holding.HoldingId, share.Holding.Symbol, share.Opening.AsOf,
                    null, share.Opening.Quantity, null, false));

            if (share.Timeline.FirstNegative() is { } negative)
                _findings.Add(new ReconciliationFinding(
                    FindingCode.NegativePosition, share.Holding.HoldingId, share.Holding.Symbol, negative.Date,
                    negative.Quantity, null, null, true));
        }
    }

    // ---------------- valuation ----------------

    private AccountValue ValueAt(DateOnly date, bool applyWindows)
    {
        var components = new List<ComponentValue>(_shares.Count + 1);
        foreach (var share in _shares) components.Add(ValueShare(share, date, applyWindows));
        components.Add(ValueCash(date, applyWindows));

        var value = components.Where(c => c.Status == HoldingValuationStatus.Covered).Sum(c => c.Value);
        var complete = components.All(c => c.Status != HoldingValuationStatus.Missing);
        return new AccountValue(date, value, complete, components);
    }

    private ComponentValue ValueShare(ShareComponent c, DateOnly date, bool applyWindows)
    {
        var h = c.Holding;
        ComponentValue Missing(MissingCause cause, decimal quantity) =>
            new(h.HoldingId, h.Symbol, quantity, 0m, HoldingValuationStatus.Missing, null, cause, null, null, null);
        ComponentValue NotHeld() =>
            new(h.HoldingId, h.Symbol, 0m, 0m, HoldingValuationStatus.NotHeld, null, null, null, null, null);
        ComponentValue Covered(decimal quantity, decimal value, ComponentSource source, decimal? unitPrice, DateOnly? priceAsOf, DateOnly? snapshotAsOf) =>
            new(h.HoldingId, h.Symbol, quantity, value, HoldingValuationStatus.Covered, source, null, unitPrice, priceAsOf, snapshotAsOf);

        var (q, started) = c.Timeline.QuantityAt(date);

        // Before the imported history, only a stored statement says what was held; otherwise a position that
        // predates the history is unknown there, not zero.
        if (OpeningDate is { } opening && date < opening && !HasStoredAnchorOnOrBefore(c.Holding.AnchorsAscending, date))
            return c.Opening.Class == OpeningClass.PreHistory ? Missing(MissingCause.BeforeHistory, 0m) : NotHeld();

        if (!started || Math.Abs(q) < ZeroQuantity) return NotHeld();
        if (q < 0m) return Missing(MissingCause.NegativePosition, q);
        if (applyWindows && c.InWindow(date)) return Missing(MissingCause.MaterialMismatch, q);

        var price = h.Prices.Recent(date, _options.MaxPriceAgeDays);
        var anchor = Latest(c.ValuedAnchors, date);
        // A snapshot saying nothing was held can't value a position the ledger says is held.
        if (anchor is { Quantity: 0m, MarketValue: 0m }) anchor = null;

        // (0) The broker's statement is ground truth while nothing newer is known.
        if (anchor is { } fresh && !c.HasActivity(fresh.AsOf, date) && (price is null || price.Value.AsOf <= fresh.AsOf))
            return Covered(q, fresh.MarketValue!.Value, ComponentSource.Snapshot, fresh.UnitPrice, fresh.AsOf, fresh.AsOf);

        // (1) Quantity × a recent close; a stable-NAV fund at its fixed price whatever the price data.
        if (price is { } p)
            return Covered(q, q * p.Close, ComponentSource.Price, p.Close, p.AsOf, null);
        if (h.Prices.StablePrice is { } stable)
            return Covered(q, q * stable, ComponentSource.StableNav, stable, null, null);

        // (2) Carry the last statement forward, revalued at its per-share price if shares changed since.
        if (anchor is { } last)
        {
            if (last.UnitPrice is { } unit)
                return Covered(q, q * unit, ComponentSource.Snapshot, unit, last.AsOf, last.AsOf);
            return Covered(q, last.MarketValue!.Value, ComponentSource.Snapshot, null, last.AsOf, last.AsOf); // value-only
        }

        return Missing(h.Prices.OnOrBefore(date) is null ? MissingCause.NoPrice : MissingCause.StalePrice, q);
    }

    private ComponentValue ValueCash(DateOnly date, bool applyWindows)
    {
        if (OpeningDate is { } opening && date < opening && !_cash.HasStoredAnchorOnOrBefore(date))
        {
            return _cash.Opening.Class == OpeningClass.PreHistory
                ? new(null, null, 0m, 0m, HoldingValuationStatus.Missing, null, MissingCause.BeforeHistory, null, null, null)
                : new(null, null, 0m, 0m, HoldingValuationStatus.NotHeld, null, null, null, null, null);
        }

        var (cash, _) = _cash.Timeline.QuantityAt(date);
        if (Math.Abs(cash) < ZeroCash)
            return new(null, null, 0m, 0m, HoldingValuationStatus.NotHeld, null, null, null, null, null);
        if (applyWindows && _cash.InWindow(date))
            return new(null, null, cash, 0m, HoldingValuationStatus.Missing, null, MissingCause.MaterialMismatch, null, null, null);
        return new(null, null, cash, cash, HoldingValuationStatus.Covered, ComponentSource.Cash, null, 1m, null, null);
    }

    private ExternalFlow ToFlow(LedgerRow row)
    {
        var kind = row.IsImplied ? FlowKind.Implied
            : row.Type switch
            {
                TransactionType.Deposit => FlowKind.Deposit,
                TransactionType.Withdrawal => FlowKind.Withdrawal,
                _ => FlowKind.CashTransfer,
            };

        // An in-kind transfer of a security moves shares, not cash: value it on the day (QFX reports no amount).
        if (row.Type == TransactionType.Transfer
            && row.HoldingId is { } holdingId
            && _shareByHolding.TryGetValue(holdingId, out var share)
            && row.Quantity is { } qty && qty != 0m)
        {
            if (row.Amount != 0m) return new ExternalFlow(row.TradeDate, row.Amount, FlowKind.InKindTransfer, row.TransactionId, true);

            var unit = share.Holding.Prices.Recent(row.TradeDate, _options.MaxPriceAgeDays)?.Close
                       ?? row.Price
                       ?? Latest(share.ValuedAnchors, row.TradeDate)?.UnitPrice
                       ?? share.Holding.Prices.StablePrice;
            return unit is { } u
                ? new ExternalFlow(row.TradeDate, qty * u, FlowKind.InKindTransfer, row.TransactionId, true)
                : new ExternalFlow(row.TradeDate, 0m, FlowKind.InKindTransfer, row.TransactionId, false);
        }

        return new ExternalFlow(row.TradeDate, row.Amount, kind, row.TransactionId, true);
    }

    // ---------------- component state ----------------

    private sealed record Mismatch(DateOnly Date, DateOnly? PreviousAnchor, decimal Rolled, decimal Anchor, decimal? AnchorUnitPrice);

    /// <summary>A quantity (shares or cash) over time: one end-of-day value per date anything changed.</summary>
    private sealed class Timeline
    {
        private readonly List<DateOnly> _dates = [];
        private readonly List<decimal> _quantities = [];

        public List<Mismatch> Mismatches { get; } = [];

        public void Add(DateOnly date, decimal quantity)
        {
            _dates.Add(date);
            _quantities.Add(quantity);
        }

        /// <summary>The end-of-day quantity on <paramref name="date"/>; <c>started</c> is false before anything happened.</summary>
        public (decimal Quantity, bool Started) QuantityAt(DateOnly date)
        {
            var index = _dates.BinarySearch(date);
            if (index < 0) index = ~index - 1;
            return index < 0 ? (0m, false) : (_quantities[index], true);
        }

        public (DateOnly Date, decimal Quantity)? FirstNegative()
        {
            for (var i = 0; i < _dates.Count; i++)
                if (_quantities[i] <= -ZeroQuantity) return (_dates[i], _quantities[i]);
            return null;
        }
    }

    private abstract class Component
    {
        public Timeline Timeline { get; set; } = new();

        /// <summary>Exclusive date ranges where a material drift makes the component untrustworthy.</summary>
        public List<(DateOnly? After, DateOnly Before)> Windows { get; } = [];

        public DerivedOpening Opening { get; set; } = null!;

        public bool InWindow(DateOnly date)
            => Windows.Any(w => (w.After is null || date > w.After.Value) && date < w.Before);
    }

    private sealed class ShareComponent(HoldingInput holding, List<LedgerRow> rows) : Component
    {
        public HoldingInput Holding { get; } = holding;

        public List<LedgerRow> Rows { get; } = rows;

        public DateOnly[] ActivityDates { get; set; } = [];

        public List<PositionAnchor> ValuedAnchors { get; set; } = [];

        /// <summary>True when shares changed in <c>(after, date]</c>.</summary>
        public bool HasActivity(DateOnly after, DateOnly date)
        {
            var index = Array.BinarySearch(ActivityDates, after);
            index = index < 0 ? ~index : index + 1; // first activity strictly after `after`
            return index < ActivityDates.Length && ActivityDates[index] <= date;
        }
    }

    private sealed class CashComponent : Component
    {
        public List<DateOnly> StoredAnchorDates { get; init; } = [];

        public bool HasStoredAnchorOnOrBefore(DateOnly date) => StoredAnchorDates.Any(d => d <= date);
    }
}
