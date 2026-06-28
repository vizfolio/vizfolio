using Shouldly;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.Domain;

public sealed class AccountTests
{
    [Fact]
    public void Constructor_assigns_id_and_required_fields()
    {
        var portfolioId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var account = new Account(portfolioId, "Fidelity Brokerage", "Fidelity", "1234", "Brokerage");

        account.AccountId.ShouldNotBe(Guid.Empty);
        account.PortfolioId.ShouldBe(portfolioId);
        account.Name.ShouldBe("Fidelity Brokerage");
        account.Institution.ShouldBe("Fidelity");
        account.AccountNumber.ShouldBe("1234");
        account.AccountType.ShouldBe("Brokerage");
        account.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void Constructor_trims_whitespace_on_strings()
    {
        var account = new Account(Guid.NewGuid(), "  Name  ", "  Inst  ", "  9999  ", "  Roth  ");

        account.Name.ShouldBe("Name");
        account.Institution.ShouldBe("Inst");
        account.AccountNumber.ShouldBe("9999");
        account.AccountType.ShouldBe("Roth");
    }

    [Fact]
    public void Constructor_treats_blank_account_type_as_null()
    {
        var account = new Account(Guid.NewGuid(), "n", "i", "a", "   ");

        account.AccountType.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_empty_portfolio_id()
    {
        Should.Throw<ArgumentException>(() =>
            new Account(Guid.Empty, "n", "i", "a"));
    }

    [Theory]
    [InlineData("", "Inst", "1234")]
    [InlineData("   ", "Inst", "1234")]
    [InlineData("Name", "", "1234")]
    [InlineData("Name", "Inst", "")]
    public void Constructor_rejects_blank_required_fields(string name, string institution, string accountNumber)
    {
        Should.Throw<ArgumentException>(() =>
            new Account(Guid.NewGuid(), name, institution, accountNumber));
    }

    [Fact]
    public void Rename_updates_name()
    {
        var account = new Account(Guid.NewGuid(), "Old", "i", "a");

        account.Rename("New");

        account.Name.ShouldBe("New");
    }

    [Fact]
    public void SetAccountType_can_clear_type()
    {
        var account = new Account(Guid.NewGuid(), "n", "i", "a", "Brokerage");

        account.SetAccountType(null);

        account.AccountType.ShouldBeNull();
    }
}
