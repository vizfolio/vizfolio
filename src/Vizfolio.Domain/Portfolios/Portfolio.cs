namespace Vizfolio.Domain.Portfolios;

public sealed class Portfolio
{
    private Portfolio() { }

    public Portfolio(string name)
    {
        PortfolioId = Guid.NewGuid();
        Rename(name);
    }

    public Guid PortfolioId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Portfolio name cannot be empty.", nameof(name));

        Name = name.Trim();
    }
}
