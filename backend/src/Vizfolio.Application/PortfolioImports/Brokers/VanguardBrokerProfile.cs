namespace Vizfolio.Application.PortfolioImports.Brokers;

/// <summary>
/// Vanguard (<c>BROKERID</c> <c>vanguard.com</c>). Checked against real Vanguard QFX exports:
/// <list type="bullet">
///   <item>sweeps between cash and the settlement fund are <c>BUYMF</c>/<c>SELLMF</c> rows with the memo
///   "MONEY FUND PURCHASE" / "MONEY FUND REDEMPTION" (ordinary trades say "BUY"/"SELL");</item>
///   <item><c>&lt;AVAILCASH&gt;</c> equals the settlement fund's <c>MKTVAL</c> in <c>&lt;INVPOSLIST&gt;</c> — it <i>is</i>
///   the settlement fund, not extra cash.</item>
/// </list>
/// </summary>
public sealed class VanguardBrokerProfile : IBrokerProfile
{
    /// <summary>Vanguard's OFX <c>BROKERID</c>, which Vizfolio uses as its institution code.</summary>
    public const string InstitutionCode = "vanguard.com";

    public string Name => "Vanguard";

    public bool Matches(string? institutionCode)
        => string.Equals(institutionCode?.Trim(), InstitutionCode, StringComparison.OrdinalIgnoreCase);

    public bool? AvailableCashIncludesSettlementFund => true;

    public bool IsSweepMemo(string? memo)
        => memo is not null
           && (memo.Contains("MONEY FUND PURCHASE", StringComparison.OrdinalIgnoreCase)
               || memo.Contains("MONEY FUND REDEMPTION", StringComparison.OrdinalIgnoreCase));
}
