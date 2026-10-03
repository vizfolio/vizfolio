using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

public sealed class AccountHistoryService : IAccountHistoryService
{
    /// <summary>Symbol that addresses an account's cash in an opening balance.</summary>
    public const string CashSymbol = "$CASH";

    private readonly IAppDbContext _db;
    private readonly AccountValuationLoader _valuationLoader;

    public AccountHistoryService(IAppDbContext db, AccountValuationLoader valuationLoader)
    {
        _db = db;
        _valuationLoader = valuationLoader;
    }

    public async Task<HistoryCoverageResult?> GetCoverageAsync(
        Guid portfolioId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var accountInScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == portfolioId && a.AccountId == accountId, cancellationToken);
        if (!accountInScope) return null;

        var holdingIds = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => h.AccountId == accountId)
            .Select(h => h.AccountHoldingId)
            .ToListAsync(cancellationToken);

        var firstTx = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderBy(t => t.TradeDate)
            .Select(t => (DateOnly?)t.TradeDate)
            .FirstOrDefaultAsync(cancellationToken);

        DateOnly? earliestSnap = null;
        var openingCount = 0;
        var statementCount = 0;
        var brokerCount = 0;

        if (holdingIds.Count > 0)
        {
            // Only a snapshot with a market value can close the gap — a quantity-only snapshot
            // yields no usable balance, so performance still treats the start as incomplete.
            // Keying the gap off the earliest *valued* snapshot keeps this screen consistent
            // with the performance starting balance and the opening-balance screen.
            earliestSnap = await _db.AccountHoldingSnapshots
                .AsNoTracking()
                .Where(s => holdingIds.Contains(s.AccountHoldingId) && s.MarketValue != null)
                .OrderBy(s => s.AsOf)
                .Select(s => (DateOnly?)s.AsOf)
                .FirstOrDefaultAsync(cancellationToken);

            var counts = await _db.AccountHoldingSnapshots
                .AsNoTracking()
                .Where(s => holdingIds.Contains(s.AccountHoldingId))
                .GroupBy(s => s.Source)
                .Select(g => new { Source = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            foreach (var row in counts)
            {
                switch (row.Source)
                {
                    case AccountHoldingSnapshotSource.OpeningBalance:
                        openingCount = row.Count;
                        break;
                    case AccountHoldingSnapshotSource.Statement:
                        statementCount = row.Count;
                        break;
                    case AccountHoldingSnapshotSource.BrokerPosition:
                        brokerCount = row.Count;
                        break;
                }
            }
        }

        // Starting positions are derived from the broker's statements (rolled back over the ledger), so a history
        // that starts mid-life needs no manual opening balance. A gap remains only where that derivation fails:
        // rolling a statement back gives a negative position, i.e. the ledger and the broker disagree.
        var hasGap = false;
        if (firstTx.HasValue)
        {
            var loaded = await _valuationLoader.LoadAsync(
                [accountId], DateOnly.MaxValue, cancellationToken, includePrices: false);
            hasGap = loaded.Engines[accountId].Openings.Any(o => o.Class == OpeningClass.Inconsistent);
        }
        var suggested = firstTx.HasValue ? firstTx.Value.AddDays(-1) : (DateOnly?)null;

        return new HistoryCoverageResult(
            accountId,
            firstTx,
            earliestSnap,
            hasGap,
            suggested,
            openingCount,
            statementCount,
            brokerCount);
    }

    public async Task<OpeningBalanceResult?> SetOpeningBalanceAsync(
        Guid portfolioId,
        Guid accountId,
        OpeningBalanceCommand command,
        CancellationToken cancellationToken)
    {
        var accountInScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == portfolioId && a.AccountId == accountId, cancellationToken);
        if (!accountInScope) return null;

        var outcomes = new List<OpeningBalanceHoldingOutcome>(command.Holdings.Count);
        var created = 0;
        var updated = 0;

        foreach (var input in command.Holdings)
        {
            var currency = string.IsNullOrWhiteSpace(input.CurrencyCode)
                ? command.DefaultCurrencyCode
                : input.CurrencyCode;

            var holding = await ResolveOrCreateHoldingAsync(
                accountId, input.Symbol, input.Cusip, currency, cancellationToken);

            // A snapshot is only usable for a balance if it carries a market value (that's what
            // performance sums). When the user gives units + unit price but no explicit market
            // value, derive it so a price-only entry still yields a complete opening balance.
            var marketValue = input.MarketValue
                ?? (input.UnitPrice.HasValue ? input.Units * input.UnitPrice.Value : (decimal?)null);

            var existing = await _db.AccountHoldingSnapshots
                .FirstOrDefaultAsync(
                    s => s.AccountHoldingId == holding.AccountHoldingId && s.AsOf == command.AsOf,
                    cancellationToken);

            bool wasCreated;
            if (existing is not null)
            {
                _db.AccountHoldingSnapshots.Remove(existing);
                wasCreated = false;
                updated++;
            }
            else
            {
                wasCreated = true;
                created++;
            }

            var snapshot = new AccountHoldingSnapshot(
                holding.AccountHoldingId,
                command.AsOf,
                input.Units,
                AccountHoldingSnapshotSource.OpeningBalance);
            snapshot.SetValuation(input.CostBasis, marketValue, input.UnitPrice, currency);
            _db.AccountHoldingSnapshots.Add(snapshot);

            outcomes.Add(new OpeningBalanceHoldingOutcome(
                input.Symbol.Trim().ToUpperInvariant(),
                holding.AccountHoldingId,
                input.Units,
                marketValue,
                input.UnitPrice,
                input.CostBasis,
                currency,
                wasCreated));
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new OpeningBalanceResult(accountId, command.AsOf, created, updated, outcomes);
    }

    private async Task<AccountHolding> ResolveOrCreateHoldingAsync(
        Guid accountId,
        string symbol,
        string? cusip,
        string? currencyCode,
        CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();

        // The account's cash has one holding of its own, valued with (and anchoring) the settlement fund's cash.
        if (normalized == CashSymbol)
        {
            var cash = await _db.AccountHoldings.FirstOrDefaultAsync(
                h => h.AccountId == accountId && h.Kind == AccountHoldingKind.Cash, cancellationToken);
            if (cash is not null) return cash;

            cash = new AccountHolding(accountId, AccountHoldingKind.Cash);
            cash.SetIdentifiers(CashSymbol, name: "Cash", isin: null, cusip: null);
            cash.SetCurrency(currencyCode);
            _db.AccountHoldings.Add(cash);
            return cash;
        }

        var existing = await _db.AccountHoldings
            .FirstOrDefaultAsync(
                h => h.AccountId == accountId && h.Symbol == normalized,
                cancellationToken);
        if (existing is not null) return existing;

        // Symbol didn't match an existing per-account holding. Create as Kind=Other so the
        // opening-balance snapshot has something to attach to; a subsequent import + relinker
        // pass can promote it to Kind=Security or Kind=Fund once reference data is available.
        var newHolding = new AccountHolding(accountId, AccountHoldingKind.Other);
        newHolding.SetIdentifiers(normalized, name: null, isin: null, cusip);
        newHolding.SetCurrency(currencyCode);
        _db.AccountHoldings.Add(newHolding);
        return newHolding;
    }
}
