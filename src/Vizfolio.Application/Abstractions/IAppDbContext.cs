using Microsoft.EntityFrameworkCore;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Holdings;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Portfolio> Portfolios { get; }

    DbSet<Security> Securities { get; }

    DbSet<Fund> Funds { get; }

    DbSet<FundSnapshot> FundSnapshots { get; }

    DbSet<Holding> Holdings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
