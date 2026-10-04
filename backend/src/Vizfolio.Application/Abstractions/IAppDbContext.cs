using Microsoft.EntityFrameworkCore;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;
using Vizfolio.Domain.Settings;

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

    DbSet<MoneyMarketFund> MoneyMarketFunds { get; }

    DbSet<PriceSeriesStatus> PriceSeriesStatuses { get; }

    DbSet<AppSetting> AppSettings { get; }

    DbSet<ImportBatch> ImportBatches { get; }

    DbSet<ImportBatchRowUpdate> ImportBatchRowUpdates { get; }

    DbSet<Currency> Currencies { get; }

    DbSet<Country> Countries { get; }

    DbSet<AssetCategory> AssetCategories { get; }

    DbSet<AssetClass> AssetClasses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (every SaveChanges inside it commits or rolls back
    /// together). Joins the transaction already open, if any, so calls can nest.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);
}
