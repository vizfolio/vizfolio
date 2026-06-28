using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Holdings;
using Vizfolio.Domain.Securities;
using Vizfolio.Infrastructure.Persistence;

namespace Vizfolio.Api.Tests.Persistence;

public sealed class AppDbContextModelTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Model_includes_all_domain_aggregates()
    {
        using var context = CreateContext();

        context.Model.FindEntityType(typeof(Security)).ShouldNotBeNull();
        context.Model.FindEntityType(typeof(Fund)).ShouldNotBeNull();
        context.Model.FindEntityType(typeof(FundSnapshot)).ShouldNotBeNull();
        context.Model.FindEntityType(typeof(Holding)).ShouldNotBeNull();
        context.Model.FindEntityType(typeof(CitSubstitution)).ShouldNotBeNull();
    }

    [Fact]
    public void CitSubstitution_stores_fidelity_as_string()
    {
        using var context = CreateContext();

        var fidelity = context.Model
            .FindEntityType(typeof(CitSubstitution))!
            .FindProperty(nameof(CitSubstitution.Fidelity))!;

        fidelity.GetProviderClrType().ShouldBe(typeof(string));
    }

    [Fact]
    public void Security_has_unique_cik_index()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(Security))!;
        var cikIndex = entity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Security.Cik)));

        cikIndex.ShouldNotBeNull();
        cikIndex.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void Fund_has_unique_series_id_index()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(Fund))!;
        var seriesIndex = entity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Fund.SeriesId)));

        seriesIndex.ShouldNotBeNull();
        seriesIndex.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void FundSnapshot_has_unique_composite_index_on_fund_and_as_of()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(FundSnapshot))!;
        var compositeIndex = entity.GetIndexes()
            .FirstOrDefault(i =>
                i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == nameof(FundSnapshot.FundId)) &&
                i.Properties.Any(p => p.Name == nameof(FundSnapshot.AsOf)));

        compositeIndex.ShouldNotBeNull();
        compositeIndex.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void Schema_creates_successfully_on_sqlite()
    {
        using var context = CreateContext();
        context.Database.OpenConnection();

        var created = context.Database.EnsureCreated();

        created.ShouldBeTrue();
    }

    [Fact]
    public void Holding_has_foreign_key_to_CitSubstitution()
    {
        using var context = CreateContext();

        var holding = context.Model.FindEntityType(typeof(Holding))!;
        var fk = holding.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(CitSubstitution));

        fk.ShouldNotBeNull();
        fk.Properties.ShouldHaveSingleItem().Name.ShouldBe(nameof(Holding.CitSubstitutionId));
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        fk.IsRequired.ShouldBeFalse();
    }
}
