namespace Vizfolio.Domain.Funds;

/// <summary>
/// Reference data for one money market fund series, from its latest SEC Form N-MFP (via fund-extracts'
/// <c>money_market_funds.json</c>). Tells valuation which tickers are money market funds and whether each keeps a
/// stable price per share — and what that price is (usually $1.00; some funds and ETFs use $10 or $100).
/// </summary>
public sealed class MoneyMarketFund
{
    private readonly List<string> _tickers = [];

    private MoneyMarketFund() { }

    public MoneyMarketFund(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
            throw new ArgumentException("Series ID is required.", nameof(seriesId));

        MoneyMarketFundId = Guid.NewGuid();
        SeriesId = seriesId.Trim().ToUpperInvariant();
    }

    public Guid MoneyMarketFundId { get; private set; }

    public string SeriesId { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? RegistrantCik { get; private set; }

    /// <summary>N-MFP fund category, e.g. Government, Prime, Single State, Other Tax Exempt.</summary>
    public string? Category { get; private set; }

    /// <summary>The fund's own statement that it seeks a stable price per share (N-MFP <c>seekStablePricePerShare</c>).</summary>
    public bool SeeksStablePrice { get; private set; }

    /// <summary>The stable price it seeks (N-MFP <c>stablePricePerShare</c>); null for a floating-NAV fund.</summary>
    public decimal? StablePricePerShare { get; private set; }

    public bool? IsRetail { get; private set; }

    /// <summary>Report date of the N-MFP filing this row reflects.</summary>
    public DateOnly AsOf { get; private set; }

    public string SourceFiling { get; private set; } = string.Empty;

    public IReadOnlyList<string> Tickers => _tickers;

    /// <summary>The price per share valuation can rely on, or null when the fund floats.</summary>
    public decimal? StablePrice => SeeksStablePrice && StablePricePerShare is > 0m ? StablePricePerShare : null;

    public void Update(
        string? name,
        string? registrantCik,
        string? category,
        bool seeksStablePrice,
        decimal? stablePricePerShare,
        bool? isRetail,
        DateOnly asOf,
        string sourceFiling,
        IEnumerable<string> tickers)
    {
        ArgumentNullException.ThrowIfNull(tickers);
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        RegistrantCik = string.IsNullOrWhiteSpace(registrantCik) ? null : registrantCik.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        SeeksStablePrice = seeksStablePrice;
        StablePricePerShare = stablePricePerShare;
        IsRetail = isRetail;
        AsOf = asOf;
        SourceFiling = sourceFiling.Trim();
        _tickers.Clear();
        _tickers.AddRange(tickers
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant())
            .Distinct());
    }
}
