namespace Vizfolio.Domain.Pricing;

/// <summary>
/// How a price/corporate-action series is keyed. A series is shared reference data (a security's
/// close prices), not per-account. Most series resolve to a linked <c>Security</c>; holdings with
/// no linked Security/Fund fall back to a symbol-based key.
/// </summary>
public enum PriceSeriesKind
{
    /// <summary>Keyed by <c>Security.SecurityId</c>.</summary>
    Security,

    /// <summary>Keyed by an uppercased symbol (or <c>CUSIP:xxxx</c>) when no Security is linked.</summary>
    Symbol
}
