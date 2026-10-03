using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

public sealed class AccountHoldingResolver
{
    private readonly IAppDbContext _db;
    private readonly ILogger _logger;

    private readonly Dictionary<string, Guid> _securityIdByTicker = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _fundIdByTicker = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid SecurityId, string? Symbol), AccountHolding> _holdingBySecurity = new();
    private readonly Dictionary<(Guid FundId, string? Symbol), AccountHolding> _holdingByFund = new();
    private readonly Dictionary<string, AccountHolding> _unclassifiedBySymbol = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccountHolding> _unclassifiedByCusip = new(StringComparer.Ordinal);

    private Guid _accountId;
    private bool _primed;

    public AccountHoldingResolver(IAppDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task PrimeAsync(Guid accountId, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        _accountId = accountId;
        _primed = true;

        var tickerSet = tickers.ToHashSet(StringComparer.Ordinal);

        var existingHoldings = await _db.AccountHoldings
            .Where(h => h.AccountId == accountId)
            .ToListAsync(cancellationToken);
        foreach (var holding in existingHoldings)
        {
            if (holding.SecurityId.HasValue)
                _holdingBySecurity.TryAdd((holding.SecurityId.Value, holding.Symbol), holding);
            if (holding.FundId.HasValue)
                _holdingByFund.TryAdd((holding.FundId.Value, holding.Symbol), holding);
            if (holding.Kind == AccountHoldingKind.Other)
                RegisterUnclassified(holding);
        }

        if (tickerSet.Count == 0) return;

        var securities = await _db.Securities.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var security in securities)
        {
            foreach (var ticker in security.Tickers)
            {
                if (tickerSet.Contains(ticker))
                    _securityIdByTicker.TryAdd(ticker, security.SecurityId);
            }
        }

        var snapshots = await _db.FundSnapshots.AsNoTracking().ToListAsync(cancellationToken);
        var latestByFund = snapshots
            .GroupBy(s => s.FundId)
            .Select(g => g.OrderByDescending(s => s.AsOf).First());

        foreach (var snapshot in latestByFund)
        {
            foreach (var shareClass in snapshot.ShareClasses)
            {
                if (shareClass.Ticker is null) continue;
                if (!tickerSet.Contains(shareClass.Ticker)) continue;
                if (_securityIdByTicker.ContainsKey(shareClass.Ticker))
                {
                    _logger.LogWarning(
                        "Ticker {Ticker} matches both Security and Fund {FundId}; preferring Security.",
                        shareClass.Ticker, snapshot.FundId);
                    continue;
                }
                _fundIdByTicker.TryAdd(shareClass.Ticker, snapshot.FundId);
            }
        }
    }

    /// <summary>
    /// The holding for a ticker that reference data (a Security or a fund share class) recognises, or null when
    /// it doesn't. An unclassified holding already carrying the ticker is promoted rather than duplicated.
    /// </summary>
    public AccountHolding? ResolveByTicker(string ticker, string? cusip, string? currencyCode)
    {
        EnsurePrimed();

        if (_securityIdByTicker.TryGetValue(ticker, out var securityId))
            return PromoteOrGetForSecurity(securityId, ticker, cusip, currencyCode);
        if (_fundIdByTicker.TryGetValue(ticker, out var fundId))
            return PromoteOrGetForFund(fundId, ticker, cusip, currencyCode);
        return null;
    }

    /// <summary>
    /// The holding for a row's security, creating one when needed so that no share-bearing row is left out of
    /// valuation: a ticker reference data recognises resolves as usual; anything else gets an unclassified
    /// (<see cref="AccountHoldingKind.Other"/>) holding keyed by its ticker — or by CUSIP when there is no
    /// ticker — which a later relink promotes once reference data catches up. Null only when the row identifies
    /// no security at all.
    /// </summary>
    public AccountHolding? ResolveOrCreate(string? ticker, string? cusip, string? currencyCode)
    {
        EnsurePrimed();

        var symbol = Normalize(ticker);
        var normalizedCusip = Normalize(cusip);

        if (symbol is not null)
            return ResolveByTicker(symbol, normalizedCusip, currencyCode)
                ?? GetOrCreateUnclassified(symbol, normalizedCusip, currencyCode);

        if (normalizedCusip is null) return null;
        return _unclassifiedByCusip.TryGetValue(normalizedCusip, out var byCusip)
            ? byCusip
            : GetOrCreateUnclassified(symbol: null, normalizedCusip, currencyCode);
    }

    public AccountHolding GetOrCreateForSecurity(Guid securityId, string? symbol, string? cusip, string? currencyCode)
    {
        EnsurePrimed();
        var key = (securityId, Normalize(symbol));
        if (_holdingBySecurity.TryGetValue(key, out var existing)) return existing;

        var holding = new AccountHolding(_accountId, AccountHoldingKind.Security);
        holding.LinkToSecurity(securityId);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip);
        holding.SetCurrency(currencyCode);
        _db.AccountHoldings.Add(holding);
        _holdingBySecurity[key] = holding;
        return holding;
    }

    public AccountHolding GetOrCreateForFund(Guid fundId, string? symbol, string? cusip, string? currencyCode)
    {
        EnsurePrimed();
        var key = (fundId, Normalize(symbol));
        if (_holdingByFund.TryGetValue(key, out var existing)) return existing;

        var holding = new AccountHolding(_accountId, AccountHoldingKind.Fund);
        holding.LinkToFund(fundId);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip);
        holding.SetCurrency(currencyCode);
        _db.AccountHoldings.Add(holding);
        _holdingByFund[key] = holding;
        return holding;
    }

    // A classified holding for the security wins; otherwise an unclassified one with the same ticker is promoted
    // in place (keeping its rows and snapshots); otherwise a new holding is created.
    private AccountHolding PromoteOrGetForSecurity(Guid securityId, string ticker, string? cusip, string? currencyCode)
    {
        var key = (securityId, Normalize(ticker));
        if (_holdingBySecurity.ContainsKey(key) || !_unclassifiedBySymbol.Remove(key.Item2!, out var unclassified))
            return GetOrCreateForSecurity(securityId, ticker, cusip, currencyCode);

        unclassified.PromoteToSecurity(securityId);
        ForgetCusip(unclassified);
        _holdingBySecurity[key] = unclassified;
        return unclassified;
    }

    private AccountHolding PromoteOrGetForFund(Guid fundId, string ticker, string? cusip, string? currencyCode)
    {
        var key = (fundId, Normalize(ticker));
        if (_holdingByFund.ContainsKey(key) || !_unclassifiedBySymbol.Remove(key.Item2!, out var unclassified))
            return GetOrCreateForFund(fundId, ticker, cusip, currencyCode);

        unclassified.PromoteToFund(fundId);
        ForgetCusip(unclassified);
        _holdingByFund[key] = unclassified;
        return unclassified;
    }

    private AccountHolding GetOrCreateUnclassified(string? symbol, string? cusip, string? currencyCode)
    {
        if (symbol is not null && _unclassifiedBySymbol.TryGetValue(symbol, out var existing)) return existing;

        var holding = new AccountHolding(_accountId, AccountHoldingKind.Other);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip);
        holding.SetCurrency(currencyCode);
        _db.AccountHoldings.Add(holding);
        RegisterUnclassified(holding);
        _logger.LogInformation(
            "Created unclassified holding {Symbol} ({Cusip}) in account {AccountId}: no reference data matched it.",
            symbol, cusip, _accountId);
        return holding;
    }

    private void RegisterUnclassified(AccountHolding holding)
    {
        if (holding.Symbol is not null) _unclassifiedBySymbol.TryAdd(holding.Symbol, holding);
        else if (holding.Cusip is not null) _unclassifiedByCusip.TryAdd(holding.Cusip, holding);
    }

    private void ForgetCusip(AccountHolding holding)
    {
        if (holding.Cusip is not null && _unclassifiedByCusip.TryGetValue(holding.Cusip, out var h) && h == holding)
            _unclassifiedByCusip.Remove(holding.Cusip);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private void EnsurePrimed()
    {
        if (!_primed)
            throw new InvalidOperationException("AccountHoldingResolver must be primed before resolving.");
    }
}
