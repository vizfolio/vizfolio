namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>
/// Transaction fingerprints counted with multiplicity, so each stored row can stand for at most one incoming row: two
/// identical same-day purchases in a file match two stored rows, not one. Shared by deduplication
/// (<see cref="LedgerMatcher"/>) and account routing (<see cref="AccountRoutingAnalyzer"/>) so they agree on what "the
/// same transaction" means.
/// </summary>
internal static class FingerprintMultiset
{
    public static Dictionary<string, int> Count(IEnumerable<string> fingerprints)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fingerprint in fingerprints)
            counts[fingerprint] = counts.GetValueOrDefault(fingerprint) + 1;
        return counts;
    }

    /// <summary>How many of <paramref name="incoming"/> are covered by <paramref name="stored"/>, one for one.</summary>
    public static int Overlap(IReadOnlyDictionary<string, int> incoming, IReadOnlyDictionary<string, int> stored)
        => incoming.Sum(pair => Math.Min(pair.Value, stored.GetValueOrDefault(pair.Key)));
}
