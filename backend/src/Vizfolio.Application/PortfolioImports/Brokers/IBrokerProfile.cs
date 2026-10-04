namespace Vizfolio.Application.PortfolioImports.Brokers;

/// <summary>
/// What one broker does differently in its OFX/QFX files — knowledge that can't be read from the file format itself.
/// Matched by the statement's <c>BROKERID</c> (the account's institution code). Add a broker by implementing this and
/// registering it in <c>DependencyInjection.AddApplication</c>; brokers without a profile get
/// <see cref="DefaultBrokerProfile"/>. Holds no ticker lists: settlement funds are recognised from how the broker
/// labels and reports them.
/// </summary>
public interface IBrokerProfile
{
    /// <summary>Short display name, e.g. "Vanguard".</summary>
    string Name { get; }

    /// <summary>True when this profile describes the broker with this (lower-case) institution code.</summary>
    bool Matches(string? institutionCode);

    /// <summary>
    /// True when the statement's <c>AVAILCASH</c> is the settlement fund's position (or already contains it), so the
    /// position — not <c>AVAILCASH</c> — anchors the account's cash. Null to decide from the statement: a position
    /// whose market value equals <c>AVAILCASH</c> is taken to be the settlement fund.
    /// </summary>
    bool? AvailableCashIncludesSettlementFund { get; }

    /// <summary>True when a transaction memo marks a sweep between cash and the settlement fund.</summary>
    bool IsSweepMemo(string? memo);
}

/// <summary>Any broker without a profile: no sweep labels known, and <c>AVAILCASH</c> judged from the statement.</summary>
public sealed class DefaultBrokerProfile : IBrokerProfile
{
    public static readonly DefaultBrokerProfile Instance = new();

    public string Name => "Generic OFX";

    public bool Matches(string? institutionCode) => true;

    public bool? AvailableCashIncludesSettlementFund => null;

    public bool IsSweepMemo(string? memo) => false;
}
