using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class FundSnapshotConfiguration : IEntityTypeConfiguration<FundSnapshot>
{
    private const string MoneyType = "decimal(28,4)";
    private const string PercentageType = "decimal(18,8)";

    public void Configure(EntityTypeBuilder<FundSnapshot> builder)
    {
        builder.ToTable("FundSnapshot");
        builder.HasKey(s => s.FundSnapshotId);

        builder.Property(s => s.FundId).IsRequired();
        builder.Property(s => s.AsOf).IsRequired();
        builder.HasIndex(s => new { s.FundId, s.AsOf }).IsUnique();

        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(s => s.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.SourceFiling).IsRequired().HasMaxLength(50);
        builder.Property(s => s.SourceUrl).IsRequired().HasMaxLength(1000);

        builder.Property(s => s.NetAssetsUsd).HasColumnType(MoneyType);
        builder.Property(s => s.TotalAssetsUsd).HasColumnType(MoneyType);
        builder.Property(s => s.TotalLiabilitiesUsd).HasColumnType(MoneyType);
        builder.Property(s => s.CashNotInPortfolioUsd).HasColumnType(MoneyType);

        builder.OwnsMany(s => s.ShareClasses, sc =>
        {
            sc.ToTable("FundShareClass");
            sc.WithOwner().HasForeignKey("FundSnapshotId");
            sc.Property<int>("Id");
            sc.HasKey("Id");
            sc.Property(c => c.ClassId).IsRequired().HasMaxLength(20);
            sc.Property(c => c.Name).HasMaxLength(500);
            sc.Property(c => c.Ticker).HasMaxLength(10);
            sc.Property(c => c.ExpenseRatio).HasColumnType(PercentageType);
            sc.HasIndex("FundSnapshotId", nameof(ShareClass.ClassId)).IsUnique();
        });

        builder.Navigation(s => s.ShareClasses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(s => s.MonthlyReturns, mr =>
        {
            mr.ToTable("FundMonthlyReturn");
            mr.WithOwner().HasForeignKey("FundSnapshotId");
            mr.Property<int>("Id");
            mr.HasKey("Id");
            mr.Property(r => r.Month).IsRequired();
            mr.Property(r => r.ReturnPct).HasColumnType(PercentageType).IsRequired();
            mr.Property(r => r.ClassId).HasMaxLength(20);
            mr.HasIndex("FundSnapshotId", nameof(MonthlyReturn.Month), nameof(MonthlyReturn.ClassId));
        });

        builder.Navigation(s => s.MonthlyReturns).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
