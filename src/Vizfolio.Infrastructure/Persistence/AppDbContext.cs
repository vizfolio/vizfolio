using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Portfolio> Portfolios => Set<Portfolio>();

    public DbSet<Security> Securities => Set<Security>();

    public DbSet<Fund> Funds => Set<Fund>();

    public DbSet<FundSnapshot> FundSnapshots => Set<FundSnapshot>();

    public DbSet<FundHolding> FundHoldings => Set<FundHolding>();

    public DbSet<CitSubstitution> CitSubstitutions => Set<CitSubstitution>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<Country> Countries => Set<Country>();

    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();

    public DbSet<AssetClass> AssetClasses => Set<AssetClass>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
