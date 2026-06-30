using Shouldly;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Domain;

public sealed class PortfolioTests
{
    [Fact]
    public void Constructor_assigns_id_name_and_creation_timestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var portfolio = new Portfolio("Retirement");

        portfolio.PortfolioId.ShouldNotBe(Guid.Empty);
        portfolio.Name.ShouldBe("Retirement");
        portfolio.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void Constructor_trims_whitespace_from_name()
    {
        var portfolio = new Portfolio("   Growth   ");

        portfolio.Name.ShouldBe("Growth");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_empty_name(string invalid)
    {
        Should.Throw<ArgumentException>(() => new Portfolio(invalid));
    }

    [Fact]
    public void Rename_updates_name()
    {
        var portfolio = new Portfolio("Old");

        portfolio.Rename("New");

        portfolio.Name.ShouldBe("New");
    }
}
