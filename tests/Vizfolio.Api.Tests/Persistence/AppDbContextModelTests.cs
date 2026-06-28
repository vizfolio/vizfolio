using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Reference;
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
        context.Model.FindEntityType(typeof(FundHolding)).ShouldNotBeNull();
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
    public void FundSnapshot_cascade_deletes_with_Fund()
    {
        using var context = CreateContext();

        var snapshot = context.Model.FindEntityType(typeof(FundSnapshot))!;
        var fk = snapshot.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Fund));

        fk.ShouldNotBeNull();
        fk.Properties.ShouldHaveSingleItem().Name.ShouldBe(nameof(FundSnapshot.FundId));
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);
        fk.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void FundHolding_cascade_deletes_with_FundSnapshot()
    {
        using var context = CreateContext();

        var holding = context.Model.FindEntityType(typeof(FundHolding))!;
        var fk = holding.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(FundSnapshot));

        fk.ShouldNotBeNull();
        fk.Properties.ShouldHaveSingleItem().Name.ShouldBe(nameof(FundHolding.FundSnapshotId));
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);
        fk.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void FundHolding_has_index_on_IssuerCik()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(FundHolding))!;
        var issuerCikIndex = entity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(FundHolding.IssuerCik)));

        issuerCikIndex.ShouldNotBeNull();
    }

    [Fact]
    public void FundHolding_optionally_references_Security_with_restrict_delete()
    {
        using var context = CreateContext();

        var holding = context.Model.FindEntityType(typeof(FundHolding))!;
        var fk = holding.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Security));

        fk.ShouldNotBeNull();
        fk.Properties.ShouldHaveSingleItem().Name.ShouldBe(nameof(FundHolding.SecurityId));
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        fk.IsRequired.ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(Fund), "Fund")]
    [InlineData(typeof(FundSnapshot), "FundSnapshot")]
    [InlineData(typeof(FundHolding), "FundHolding")]
    [InlineData(typeof(Security), "Security")]
    [InlineData(typeof(CitSubstitution), "CitSubstitution")]
    [InlineData(typeof(Currency), "Currency")]
    [InlineData(typeof(Country), "Country")]
    [InlineData(typeof(AssetCategory), "AssetCategory")]
    [InlineData(typeof(AssetClass), "AssetClass")]
    public void Tables_use_singular_names(Type clrType, string expectedTable)
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(clrType)!;

        entity.GetTableName().ShouldBe(expectedTable);
    }

    [Theory]
    [InlineData(typeof(Currency), nameof(FundHolding.CurrencyCode))]
    [InlineData(typeof(Country), nameof(FundHolding.CountryCode))]
    [InlineData(typeof(AssetCategory), nameof(FundHolding.AssetCategoryCode))]
    [InlineData(typeof(AssetClass), nameof(FundHolding.AssetClassCode))]
    public void FundHolding_references_reference_lookup_with_restrict_delete(Type principal, string propertyName)
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(FundHolding))!;
        var fk = entity.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == principal);

        fk.ShouldNotBeNull();
        fk.Properties.ShouldHaveSingleItem().Name.ShouldBe(propertyName);
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        fk.IsRequired.ShouldBeFalse();
    }

    [Fact]
    public void Security_references_country_lookup_with_restrict_delete()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(Security))!;
        var fk = entity.GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Country));

        fk.ShouldNotBeNull();
        fk.Properties.ShouldHaveSingleItem().Name.ShouldBe(nameof(Security.CountryCode));
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        fk.IsRequired.ShouldBeFalse();
    }
}
