namespace Vizfolio.Application.Portfolios;

/// <summary>
/// Tunables for valuing holdings (bound from the <c>Valuation</c> configuration section). See
/// docs/price-history-valuation.md.
/// </summary>
public sealed class ValuationOptions
{
    public const string SectionName = "Valuation";

    /// <summary>
    /// A close older than this many calendar days (relative to the valuation date) is treated as no price, so a
    /// delisted or merged ticker is never valued at a years-old close. Stable-NAV funds are exempt.
    /// </summary>
    public int MaxPriceAgeDays { get; set; } = 10;

    /// <summary>
    /// A ledger/broker disagreement at a statement is material — making the position (or cash) missing back to the
    /// previous statement — when it's worth more than this share of the account's value at that statement…
    /// </summary>
    public decimal MismatchMaterialityPct { get; set; } = 0.005m;

    /// <summary>…and more than this amount, so rounding residue on a small account never trips it.</summary>
    public decimal MismatchMaterialityMin { get; set; } = 10m;

    /// <summary>
    /// Calendar days cash may stay below zero waiting for the account's own incoming cash (a deposit still clearing,
    /// sale proceeds settling) before the shortfall is taken as an implied contribution from outside the account.
    /// 0 makes every shortfall an implied contribution on its day. See <c>ImpliedContributionCalculator</c>.
    /// </summary>
    public int ImpliedContributionSettlementDays { get; set; } = ImpliedContributionCalculator.DefaultSettlementDays;
}
