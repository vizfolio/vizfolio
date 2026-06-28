using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Holdings;
using Vizfolio.Domain.Portfolios;
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

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<CitSubstitution> CitSubstitutions => Set<CitSubstitution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
