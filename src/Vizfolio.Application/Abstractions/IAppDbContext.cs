using Microsoft.EntityFrameworkCore;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Portfolio> Portfolios { get; }

    DbSet<Security> Securities { get; }

    DbSet<Fund> Funds { get; }

    DbSet<FundSnapshot> FundSnapshots { get; }

    DbSet<FundHolding> FundHoldings { get; }

    DbSet<CitSubstitution> CitSubstitutions { get; }

    DbSet<Currency> Currencies { get; }

    DbSet<Country> Countries { get; }

    DbSet<AssetCategory> AssetCategories { get; }

    DbSet<AssetClass> AssetClasses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
