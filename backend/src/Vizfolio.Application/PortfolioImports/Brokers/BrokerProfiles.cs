namespace Vizfolio.Application.PortfolioImports.Brokers;

/// <summary>The registered broker profiles; <see cref="For"/> falls back to <see cref="DefaultBrokerProfile"/>.</summary>
public sealed class BrokerProfiles
{
    /// <summary>The profiles Vizfolio ships with, for callers outside dependency injection (e.g. tests).</summary>
    public static readonly BrokerProfiles BuiltIn = new([new VanguardBrokerProfile()]);

    private readonly IReadOnlyList<IBrokerProfile> _profiles;

    public BrokerProfiles(IEnumerable<IBrokerProfile> profiles)
    {
        _profiles = profiles.Where(p => p is not DefaultBrokerProfile).ToList();
    }

    public IBrokerProfile For(string? institutionCode)
        => _profiles.FirstOrDefault(p => p.Matches(institutionCode)) ?? DefaultBrokerProfile.Instance;
}
