namespace Vizfolio.Application.Portfolios.Valuation;

/// <summary>
/// Recognises an account's settlement (core / sweep) fund: the money-market fund that holds its uninvested cash.
/// Its shares are cash, so it's valued as part of the account's cash rather than as an investment.
/// <para>
/// Recognised from a short list of known brokerage settlement funds, plus — for any broker — a ticker that appears
/// on a "Sweep…" row in the account's own history. This stands in for per-broker profiles (roadmap Phase 2.4),
/// which will mark settlement rows at import instead. An <i>invested</i> money-market fund (e.g. a Treasury fund
/// bought deliberately) is not a settlement fund; it's an ordinary holding valued at $1.00 a share.
/// </para>
/// </summary>
public static class SettlementFunds
{
    public static readonly IReadOnlySet<string> KnownTickers = new HashSet<string>(StringComparer.Ordinal)
    {
        // Vanguard settlement funds (VMMXX before the switch to VMFXX).
        "VMFXX", "VMMXX",
        // Fidelity core positions.
        "SPAXX", "FDRXX", "FZFXX",
    };

    /// <summary>The account's settlement-fund tickers, upper-cased.</summary>
    public static IReadOnlySet<string> Identify(IEnumerable<LedgerRow> ledger, IEnumerable<string?> holdingSymbols)
    {
        var tickers = ledger
            .Where(r => r.IsSweep && !string.IsNullOrWhiteSpace(r.Ticker))
            .Select(r => Normalize(r.Ticker!))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in holdingSymbols)
            if (symbol is not null && KnownTickers.Contains(Normalize(symbol)))
                tickers.Add(Normalize(symbol));

        return tickers;
    }

    public static bool IsSettlement(string? ticker, IReadOnlySet<string> settlementTickers)
        => !string.IsNullOrWhiteSpace(ticker) && settlementTickers.Contains(Normalize(ticker));

    private static string Normalize(string ticker) => ticker.Trim().ToUpperInvariant();
}
