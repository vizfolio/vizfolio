namespace Vizfolio.Domain.Portfolios;

public enum TransactionType
{
    Buy,
    Sell,
    Dividend,
    Interest,
    CapitalGain,
    Deposit,
    Withdrawal,
    Transfer,
    Fee,
    Reinvest,
    Split,
    Other,

    /// <summary>A distribution that returns part of the investment (OFX <c>RETOFCAP</c>): cash in, not income, not a contribution.</summary>
    ReturnOfCapital,

    /// <summary>A move between the account's own sub-accounts (OFX <c>JRNLSEC</c>/<c>JRNLFUND</c>): neutral for the account.</summary>
    Journal,
}
