using Microsoft.EntityFrameworkCore;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Portfolio> Portfolios { get; }

    DbSet<Account> Accounts { get; }

    DbSet<AccountTransaction> AccountTransactions { get; }

    DbSet<Security> Securities { get; }

    DbSet<AccountHolding> AccountHoldings { get; }

    DbSet<AccountHoldingSnapshot> AccountHoldingSnapshots { get; }

    DbSet<Fund> Funds { get; }

    DbSet<FundSnapshot> FundSnapshots { get; }

    DbSet<FundHolding> FundHoldings { get; }

    DbSet<PriceHistory> PriceHistories { get; }

    DbSet<CorporateAction> CorporateActions { get; }

    DbSet<CitSubstitution> CitSubstitutions { get; }

    DbSet<Currency> Currencies { get; }

    DbSet<Country> Countries { get; }

    DbSet<AssetCategory> AssetCategories { get; }

    DbSet<AssetClass> AssetClasses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
