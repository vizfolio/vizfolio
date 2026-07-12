using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>
/// A content-based, source-agnostic fingerprint used to deduplicate transactions across import formats
/// that carry no shared unique id (e.g. the same trade appearing in both a QFX export and a Vanguard
/// report). It hashes the economically-identifying fields — account, trade date, symbol, and the
/// <b>signed</b> quantity and amount — so a Buy (amount −) is distinguished from a Sell (amount +) while
/// staying robust to the two sources labelling the transaction type differently.
/// <para>
/// It deliberately excludes the source system and the (normalized) <see cref="TransactionType"/>: the
/// type is the field most likely to diverge between sources and would otherwise defeat the match.
/// Magnitudes are rounded to absorb representation noise between formats.
/// </para>
/// </summary>
public static class TransactionFingerprint
{
    // Fixed-decimal formats. These depend only on the numeric value, not the decimal's scale, so a value
    // that has been round-tripped through the database (which may store 800.00 as "800.0") fingerprints
    // identically to the same value freshly parsed from a file (e.g. "800.0000"). Using a scale-sensitive
    // formatter here would let a QFX row and its Vanguard twin hash differently and slip past dedup.
    private const string QuantityFormat = "F8";
    private const string AmountFormat = "F2";

    public static string Compute(
        Guid accountId, DateOnly tradeDate, string? ticker, decimal? quantity, decimal amount)
    {
        var raw = string.Join('|',
            accountId.ToString("N"),
            tradeDate.ToString("yyyy-MM-dd"),
            (ticker ?? string.Empty).Trim().ToUpperInvariant(),
            quantity.HasValue
                ? quantity.Value.ToString(QuantityFormat, CultureInfo.InvariantCulture)
                : string.Empty,
            amount.ToString(AmountFormat, CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var sb = new StringBuilder(32);
        for (var i = 0; i < 16; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    public static string Compute(Guid accountId, ParsedTransaction tx) =>
        Compute(accountId, tx.TradeDate, tx.Ticker, tx.Quantity, tx.Amount);

    public static string Compute(AccountTransaction tx) =>
        Compute(tx.AccountId, tx.TradeDate, tx.Ticker, tx.Quantity, tx.Amount);
}
