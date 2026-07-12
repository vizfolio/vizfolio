namespace Vizfolio.Domain.Pricing;

/// <summary>
/// The kind of corporate action. Only <see cref="Split"/> is handled today (it adjusts the ledger
/// quantity roll-forward); the enum exists so the set can extend (spin-offs, mergers) without churn.
/// </summary>
public enum CorporateActionType
{
    Split
}
