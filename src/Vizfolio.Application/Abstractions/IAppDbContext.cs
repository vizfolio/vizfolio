using Microsoft.EntityFrameworkCore;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Portfolio> Portfolios { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
