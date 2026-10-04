using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;
using Vizfolio.Domain.Settings;

namespace Vizfolio.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Portfolio> Portfolios => Set<Portfolio>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();

    public DbSet<Security> Securities => Set<Security>();

    public DbSet<AccountHolding> AccountHoldings => Set<AccountHolding>();

    public DbSet<AccountHoldingSnapshot> AccountHoldingSnapshots => Set<AccountHoldingSnapshot>();

    public DbSet<Fund> Funds => Set<Fund>();

    public DbSet<FundSnapshot> FundSnapshots => Set<FundSnapshot>();

    public DbSet<FundHolding> FundHoldings => Set<FundHolding>();

    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();

    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();

    public DbSet<CitSubstitution> CitSubstitutions => Set<CitSubstitution>();

    public DbSet<MoneyMarketFund> MoneyMarketFunds => Set<MoneyMarketFund>();

    public DbSet<PriceSeriesStatus> PriceSeriesStatuses => Set<PriceSeriesStatus>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ImportBatchRowUpdate> ImportBatchRowUpdates => Set<ImportBatchRowUpdate>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<Country> Countries => Set<Country>();

    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();

    public DbSet<AssetClass> AssetClasses => Set<AssetClass>();

    public async Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            await work();
            return;
        }

        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            await work();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
