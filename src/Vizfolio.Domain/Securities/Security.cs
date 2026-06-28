namespace Vizfolio.Domain.Securities;

public sealed class Security
{
    private readonly List<string> _tickers = [];
    private readonly List<string> _exchanges = [];

    private Security() { }

    public Security(string cik, DateTimeOffset edgarFetchedAt)
    {
        SecurityId = Guid.NewGuid();
        Cik = NormalizeCik(cik);
        EdgarFetchedAt = edgarFetchedAt;
    }

    public Guid SecurityId { get; private set; }

    public string Cik { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? EntityType { get; private set; }

    public string? Sector { get; private set; }

    public string? Industry { get; private set; }

    public string? Country { get; private set; }

    public DateTimeOffset EdgarFetchedAt { get; private set; }

    public IReadOnlyList<string> Tickers => _tickers;

    public IReadOnlyList<string> Exchanges => _exchanges;

    public void UpdateProfile(string? name, string? entityType, string? sector, string? industry, string? country)
    {
        Name = name;
        EntityType = entityType;
        Sector = sector;
        Industry = industry;
        Country = country;
    }

    public void SetTickers(IEnumerable<string> tickers)
    {
        ArgumentNullException.ThrowIfNull(tickers);
        _tickers.Clear();
        _tickers.AddRange(tickers
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant())
            .Distinct());
    }

    public void SetExchanges(IEnumerable<string> exchanges)
    {
        ArgumentNullException.ThrowIfNull(exchanges);
        _exchanges.Clear();
        _exchanges.AddRange(exchanges
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToUpperInvariant())
            .Distinct());
    }

    public void MarkRefreshed(DateTimeOffset edgarFetchedAt)
    {
        if (edgarFetchedAt < EdgarFetchedAt)
            throw new InvalidOperationException("Refresh timestamp must not move backwards.");

        EdgarFetchedAt = edgarFetchedAt;
    }

    private static string NormalizeCik(string cik)
    {
        if (string.IsNullOrWhiteSpace(cik))
            throw new ArgumentException("CIK is required.", nameof(cik));

        var trimmed = cik.Trim();
        if (!trimmed.All(char.IsDigit))
            throw new ArgumentException("CIK must contain only digits.", nameof(cik));

        return trimmed.TrimStart('0').PadLeft(10, '0');
    }
}
