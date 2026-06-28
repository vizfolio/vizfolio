using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Instruments;

namespace Vizfolio.Application.Instruments;

public sealed class InstrumentResolver
{
    private readonly IAppDbContext _db;
    private readonly ILogger _logger;

    private readonly Dictionary<string, Guid> _securityIdByTicker = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _fundIdByTicker = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Instrument> _instrumentBySecurity = new();
    private readonly Dictionary<Guid, Instrument> _instrumentByFund = new();

    private bool _primed;

    public InstrumentResolver(IAppDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task PrimeAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        _primed = true;
        var tickerSet = tickers.ToHashSet(StringComparer.Ordinal);

        var existingInstruments = await _db.Instruments.ToListAsync(cancellationToken);
        foreach (var instrument in existingInstruments)
        {
            if (instrument.SecurityId.HasValue)
                _instrumentBySecurity.TryAdd(instrument.SecurityId.Value, instrument);
            if (instrument.FundId.HasValue)
                _instrumentByFund.TryAdd(instrument.FundId.Value, instrument);
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

    public Instrument? ResolveByTicker(string ticker, string? cusip, string? currencyCode)
    {
        EnsurePrimed();

        if (_securityIdByTicker.TryGetValue(ticker, out var securityId))
            return GetOrCreateForSecurity(securityId, ticker, cusip, currencyCode);
        if (_fundIdByTicker.TryGetValue(ticker, out var fundId))
            return GetOrCreateForFund(fundId, ticker, cusip, currencyCode);
        return null;
    }

    public Instrument GetOrCreateForSecurity(Guid securityId, string? symbol, string? cusip, string? currencyCode)
    {
        EnsurePrimed();
        if (_instrumentBySecurity.TryGetValue(securityId, out var existing)) return existing;

        var instrument = new Instrument(InstrumentKind.Security);
        instrument.LinkToSecurity(securityId);
        instrument.SetIdentifiers(symbol, name: null, isin: null, cusip);
        instrument.SetCurrency(currencyCode);
        _db.Instruments.Add(instrument);
        _instrumentBySecurity[securityId] = instrument;
        return instrument;
    }

    public Instrument GetOrCreateForFund(Guid fundId, string? symbol, string? cusip, string? currencyCode)
    {
        EnsurePrimed();
        if (_instrumentByFund.TryGetValue(fundId, out var existing)) return existing;

        var instrument = new Instrument(InstrumentKind.Fund);
        instrument.LinkToFund(fundId);
        instrument.SetIdentifiers(symbol, name: null, isin: null, cusip);
        instrument.SetCurrency(currencyCode);
        _db.Instruments.Add(instrument);
        _instrumentByFund[fundId] = instrument;
        return instrument;
    }

    private void EnsurePrimed()
    {
        if (!_primed)
            throw new InvalidOperationException("InstrumentResolver must be primed before resolving.");
    }
}
