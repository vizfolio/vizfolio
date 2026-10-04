using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios;

/// <summary>
/// An account's single <see cref="AccountHoldingKind.Cash"/> holding (symbol <c>$CASH</c>), whose snapshots anchor the
/// account's cash — from an opening balance the user enters, or a statement's available cash.
/// </summary>
public static class CashHoldings
{
    public const string Symbol = "$CASH";

    /// <summary>The account's cash holding, created (unsaved) when it has none. <c>Created</c> is true when new.</summary>
    public static async Task<(AccountHolding Holding, bool Created)> GetOrCreateAsync(
        IAppDbContext db, Guid accountId, string? currencyCode, CancellationToken cancellationToken)
    {
        var pending = db.AccountHoldings.Local
            .FirstOrDefault(h => h.AccountId == accountId && h.Kind == AccountHoldingKind.Cash);
        if (pending is not null) return (pending, false);

        var existing = await db.AccountHoldings.FirstOrDefaultAsync(
            h => h.AccountId == accountId && h.Kind == AccountHoldingKind.Cash, cancellationToken);
        if (existing is not null) return (existing, false);

        var cash = new AccountHolding(accountId, AccountHoldingKind.Cash);
        cash.SetIdentifiers(Symbol, name: "Cash", isin: null, cusip: null);
        cash.SetCurrency(currencyCode);
        db.AccountHoldings.Add(cash);
        return (cash, true);
    }
}
