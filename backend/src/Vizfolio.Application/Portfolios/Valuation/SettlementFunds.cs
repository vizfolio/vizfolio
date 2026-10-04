using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// Recognises an account's settlement (core / sweep) fund — the money market fund that holds its uninvested cash —
/// from the account's own history, with no list of tickers. Its shares are then valued as the account's cash.
/// <para>
/// Folding a money market fund into cash only matters when its own share history can't be rolled, so that's the
/// test. A fund is the settlement fund when:
/// <list type="bullet">
///   <item>the broker marks it so — a row or position its parser flagged from the broker profile (e.g. Vanguard's
///   "MONEY FUND PURCHASE" sweeps, or the position that <i>is</i> the statement's available cash), or</item>
///   <item>its ticker appears on a "Sweep…" row (any broker that labels sweeps), or</item>
///   <item>it's a money market fund (<see cref="HoldingInput.IsMoneyMarket"/>, from the SEC N-MFP registry) and
///   either its purchases/sales/reinvestments come without share counts (e.g. Vanguard's transaction report), or
///   it appears on the broker's statements while none of its movements are in the ledger.</item>
/// </list>
/// A money market fund whose movements are all reported with share counts (e.g. a QFX recording sweeps as buys
/// and sells of the fund, from a broker with no profile) stays an ordinary holding valued at its stable price —
/// which gives the same answer.
/// </para>
/// </summary>
public static class SettlementFunds
{
    /// <summary>The account's settlement-fund tickers, upper-cased.</summary>
    public static IReadOnlySet<string> Identify(IReadOnlyList<LedgerRow> ledger, IEnumerable<HoldingInput> holdings)
    {
        var tickers = ledger
            .Where(r => (r.IsSweep || r.IsSettlementFund) && !string.IsNullOrWhiteSpace(r.Ticker))
            .Select(r => Normalize(r.Ticker!))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var holding in holdings)
            if (holding.IsSettlementFund && !string.IsNullOrWhiteSpace(holding.Symbol))
                tickers.Add(Normalize(holding.Symbol));

        var rowsByHolding = ledger
            .Where(r => r.HoldingId is not null)
            .ToLookup(r => r.HoldingId!.Value);

        foreach (var holding in holdings)
        {
            if (!holding.IsMoneyMarket || string.IsNullOrWhiteSpace(holding.Symbol)) continue;
            if (UsedAsCash(rowsByHolding[holding.HoldingId], holding.AnchorsAscending.Count > 0))
                tickers.Add(Normalize(holding.Symbol));
        }

        return tickers;
    }

    public static bool IsSettlement(string? ticker, IReadOnlySet<string> settlementTickers)
        => !string.IsNullOrWhiteSpace(ticker) && settlementTickers.Contains(Normalize(ticker));

    /// <summary>True when the fund's shares can't be rolled from the ledger, so its value must come from the cash roll.</summary>
    private static bool UsedAsCash(IEnumerable<LedgerRow> rows, bool onStatements)
    {
        var movements = rows
            .Where(r => r.Type is TransactionType.Buy or TransactionType.Sell or TransactionType.Reinvest)
            .ToList();
        if (movements.Any(r => r.Quantity is null)) return true;
        return movements.Count == 0 && onStatements;
    }

    private static string Normalize(string ticker) => ticker.Trim().ToUpperInvariant();
}
