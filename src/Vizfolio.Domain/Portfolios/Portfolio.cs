using Vizfolio.Domain.Common;

namespace Vizfolio.Domain.Portfolios;

public sealed class Portfolio : Entity
{
    private Portfolio() { }

    public Portfolio(string name)
    {
        Rename(name);
    }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Portfolio name cannot be empty.", nameof(name));

        Name = name.Trim();
    }
}
