using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>One stored snapshot, as callers of the valuation pipeline see it.</summary>
public sealed record HoldingSnapshotRow(
    Guid AccountHoldingId,
    DateOnly AsOf,
    decimal Quantity,
    decimal? MarketValue,
    decimal? UnitPrice,
    decimal? CostBasis,
    string? CurrencyCode,
    AccountHoldingSnapshotSource Source);

/// <summary>A holding's identity, as callers of the valuation pipeline see it.</summary>
public sealed record HoldingInfo(Guid AccountHoldingId, Guid AccountId, AccountHoldingKind Kind, string? Symbol);

/// <summary>
/// One <see cref="AccountStateEngine"/> per account, plus the snapshots and reporting currency they were built
/// from (callers reuse them rather than re-querying).
/// </summary>
public sealed record LoadedValuation(
    IReadOnlyDictionary<Guid, AccountStateEngine> Engines,
    IReadOnlyList<HoldingInfo> Holdings,
    IReadOnlyList<HoldingSnapshotRow> Snapshots,
    string ReportingCurrency)
{
    /// <summary>The value of all loaded accounts at the close of <paramref name="date"/>.</summary>
    public IReadOnlyList<AccountValue> ValueAt(DateOnly date) => Engines.Values.Select(e => e.ValueAt(date)).ToList();
}

/// <summary>
/// Batch-loads everything the valuation engine needs for a set of accounts — the whole ledger, every snapshot,
/// prices and splits (a handful of queries, no per-holding round-trips) — and builds an engine per account.
/// The full history is loaded regardless of the period asked about: openings are derived by rolling the earliest
/// broker position back, which needs the rows and statements that come after the period too.
/// </summary>
public sealed class AccountValuationLoader
{
    public const string DefaultCurrencyCode = "USD";

    private readonly IAppDbContext _db;
    private readonly ValuationOptions _options;

    public AccountValuationLoader(IAppDbContext db, ValuationOptions options)
    {
        _db = db;
        _options = options;
    }

    /// <param name="accountIds">Accounts to value.</param>
    /// <param name="currencyAsOf">Snapshots on/before this date decide the reporting currency.</param>
    /// <param name="includeImplied">False to leave out Vizfolio's implied contributions (used when re-deriving them).</param>
    /// <param name="includePrices">False to skip prices and splits when only positions and openings are needed.</param>
    public async Task<LoadedValuation> LoadAsync(
        IReadOnlyCollection<Guid> accountIds,
        DateOnly currencyAsOf,
        CancellationToken cancellationToken,
        bool includeImplied = true,
        bool includePrices = true)
    {
        var ids = accountIds.Distinct().ToList();

        var holdings = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => ids.Contains(h.AccountId))
            .Select(h => new { h.AccountHoldingId, h.AccountId, h.Kind, h.Symbol, h.SecurityId })
            .ToListAsync(cancellationToken);
        var holdingIds = holdings.Select(h => h.AccountHoldingId).ToList();

        var snapshots = holdingIds.Count == 0
            ? []
            : await _db.AccountHoldingSnapshots
                .AsNoTracking()
                .Where(s => holdingIds.Contains(s.AccountHoldingId))
                .Select(s => new HoldingSnapshotRow(
                    s.AccountHoldingId, s.AsOf, s.Quantity, s.MarketValue, s.UnitPrice, s.CostBasis, s.CurrencyCode, s.Source))
                .ToListAsync(cancellationToken);

        var ledgerQuery = _db.AccountTransactions.AsNoTracking().Where(t => ids.Contains(t.AccountId));
        if (!includeImplied)
            ledgerQuery = ledgerQuery.Where(t => t.SourceSystem != ImpliedContributionService.SourceSystem);
        var ledger = await ledgerQuery
            .Select(t => new
            {
                t.AccountId,
                Row = new LedgerRow(
                    t.AccountTransactionId, t.SourceSystem, t.TradeDate, t.SettlementDate, t.Type, t.AccountHoldingId,
                    t.Ticker, t.Quantity, t.Amount, t.Price, t.SourceType),
            })
            .ToListAsync(cancellationToken);

        var currency = ResolveReportingCurrency(snapshots.Where(s => s.AsOf <= currencyAsOf));

        var securityIds = holdings.Where(h => h.SecurityId != null).Select(h => h.SecurityId!.Value).Distinct().ToList();
        var symbolKeys = holdings.Where(h => h.SecurityId == null && h.Symbol != null)
            .Select(h => h.Symbol!.ToUpperInvariant()).Distinct().ToList();

        var pricesBySecurity = new Dictionary<Guid, List<PricePointData>>();
        var pricesBySymbol = new Dictionary<string, List<PricePointData>>(StringComparer.Ordinal);
        var splitsBySecurity = new Dictionary<Guid, List<SplitAction>>();
        var splitsBySymbol = new Dictionary<string, List<SplitAction>>(StringComparer.Ordinal);
        if (includePrices && (securityIds.Count > 0 || symbolKeys.Count > 0))
        {
            // Raw closes only (an adjusted series doesn't match as-traded quantities), in the reporting currency
            // (a null currency, e.g. from the keyless source, is treated as matching).
            var priceRows = await _db.PriceHistories
                .AsNoTracking()
                .Where(p => !p.Adjusted
                            && (p.CurrencyCode == null || p.CurrencyCode == currency)
                            && ((p.SecurityId != null && securityIds.Contains(p.SecurityId!.Value))
                                || (p.SymbolKey != null && symbolKeys.Contains(p.SymbolKey!))))
                .Select(p => new { p.SecurityId, p.SymbolKey, p.AsOf, p.Close })
                .ToListAsync(cancellationToken);
            foreach (var p in priceRows)
            {
                var point = new PricePointData(p.AsOf, p.Close);
                if (p.SecurityId is { } sid) Add(pricesBySecurity, sid, point);
                else if (p.SymbolKey is { } key) Add(pricesBySymbol, key, point);
            }

            var splitRows = await _db.CorporateActions
                .AsNoTracking()
                .Where(c => c.Type == CorporateActionType.Split
                            && ((c.SecurityId != null && securityIds.Contains(c.SecurityId!.Value))
                                || (c.SymbolKey != null && symbolKeys.Contains(c.SymbolKey!))))
                .Select(c => new { c.SecurityId, c.SymbolKey, c.ExDate, c.SplitNumerator, c.SplitDenominator })
                .ToListAsync(cancellationToken);
            foreach (var c in splitRows)
            {
                if (c.SplitDenominator == 0m) continue;
                var split = new SplitAction(c.ExDate, c.SplitNumerator / c.SplitDenominator);
                if (c.SecurityId is { } sid) Add(splitsBySecurity, sid, split);
                else if (c.SymbolKey is { } key) Add(splitsBySymbol, key, split);
            }
        }

        // Which tickers are money market funds, and at what stable price — from the SEC N-MFP registry (a few
        // hundred rows, loaded whole).
        var moneyMarkets = await _db.MoneyMarketFunds.AsNoTracking().ToListAsync(cancellationToken);
        var stablePriceByTicker = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var fund in moneyMarkets)
            foreach (var ticker in fund.Tickers)
                stablePriceByTicker.TryAdd(ticker, fund.StablePrice);

        var snapshotsByHolding = snapshots.GroupBy(s => s.AccountHoldingId).ToDictionary(g => g.Key, g => g.ToList());
        var ledgerByAccount = ledger.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList());
        var holdingsByAccount = holdings.GroupBy(h => h.AccountId).ToDictionary(g => g.Key, g => g.ToList());

        var engines = new Dictionary<Guid, AccountStateEngine>();
        foreach (var accountId in ids)
        {
            var inputs = new List<HoldingInput>();
            foreach (var h in holdingsByAccount.GetValueOrDefault(accountId) ?? [])
            {
                var symbolKey = h.Symbol?.ToUpperInvariant();
                var points = h.SecurityId is { } sid ? pricesBySecurity.GetValueOrDefault(sid)
                    : symbolKey is not null ? pricesBySymbol.GetValueOrDefault(symbolKey) : null;
                var splits = h.SecurityId is { } sid2 ? splitsBySecurity.GetValueOrDefault(sid2)
                    : symbolKey is not null ? splitsBySymbol.GetValueOrDefault(symbolKey) : null;
                var anchors = (snapshotsByHolding.GetValueOrDefault(h.AccountHoldingId) ?? [])
                    .OrderBy(s => s.AsOf)
                    .Select(s => new PositionAnchor(s.AsOf, s.Quantity, s.MarketValue, ToAnchorSource(s.Source)))
                    .ToList();

                var priceList = (IReadOnlyCollection<PricePointData>?)points ?? [];
                decimal? registryPrice = null;
                var isMoneyMarket = symbolKey is not null && stablePriceByTicker.TryGetValue(symbolKey, out registryPrice);
                inputs.Add(new HoldingInput(
                    h.AccountHoldingId,
                    h.Symbol,
                    h.Kind,
                    new PriceSeries(priceList, StablePrices.Resolve(registryPrice, priceList)),
                    (IReadOnlyList<SplitAction>?)splits ?? [],
                    anchors,
                    isMoneyMarket));
            }

            engines[accountId] = new AccountStateEngine(new AccountValuationInput(
                accountId, ledgerByAccount.GetValueOrDefault(accountId) ?? [], inputs, _options));
        }

        return new LoadedValuation(
            engines,
            holdings.Select(h => new HoldingInfo(h.AccountHoldingId, h.AccountId, h.Kind, h.Symbol)).ToList(),
            snapshots,
            currency);
    }

    private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(value);
    }

    private static AnchorSource ToAnchorSource(AccountHoldingSnapshotSource source) => source switch
    {
        AccountHoldingSnapshotSource.OpeningBalance => AnchorSource.OpeningBalance,
        AccountHoldingSnapshotSource.Statement => AnchorSource.Statement,
        _ => AnchorSource.BrokerPosition,
    };

    /// <summary>
    /// The mode of the snapshots' currencies (ties broken ordinally), defaulting to USD. A single reporting
    /// currency — multi-currency conversion is out of scope.
    /// </summary>
    private static string ResolveReportingCurrency(IEnumerable<HoldingSnapshotRow> snapshots)
        => snapshots
               .Where(s => !string.IsNullOrWhiteSpace(s.CurrencyCode))
               .GroupBy(s => s.CurrencyCode!)
               .OrderByDescending(g => g.Count())
               .ThenBy(g => g.Key, StringComparer.Ordinal)
               .Select(g => g.Key)
               .FirstOrDefault()
           ?? DefaultCurrencyCode;
}
