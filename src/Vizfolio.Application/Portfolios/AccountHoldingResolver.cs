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

    public AccountHolding? ResolveByTicker(string ticker, string? cusip, string? currencyCode)
    {
        EnsurePrimed();

        if (_securityIdByTicker.TryGetValue(ticker, out var securityId))
            return GetOrCreateForSecurity(securityId, ticker, cusip, currencyCode);
        if (_fundIdByTicker.TryGetValue(ticker, out var fundId))
            return GetOrCreateForFund(fundId, ticker, cusip, currencyCode);
        return null;
    }

    public AccountHolding GetOrCreateForSecurity(Guid securityId, string? symbol, string? cusip, string? currencyCode)
    {
        EnsurePrimed();
        var key = (securityId, NormalizeSymbol(symbol));
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
        var key = (fundId, NormalizeSymbol(symbol));
        if (_holdingByFund.TryGetValue(key, out var existing)) return existing;

        var holding = new AccountHolding(_accountId, AccountHoldingKind.Fund);
        holding.LinkToFund(fundId);
        holding.SetIdentifiers(symbol, name: null, isin: null, cusip);
        holding.SetCurrency(currencyCode);
        _db.AccountHoldings.Add(holding);
        _holdingByFund[key] = holding;
        return holding;
    }

    private static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private void EnsurePrimed()
    {
        if (!_primed)
            throw new InvalidOperationException("AccountHoldingResolver must be primed before resolving.");
    }
}
