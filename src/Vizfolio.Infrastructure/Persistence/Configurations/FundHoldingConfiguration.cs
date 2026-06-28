using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class FundHoldingConfiguration : IEntityTypeConfiguration<FundHolding>
{
    private const string MoneyType = "decimal(28,4)";
    private const string PercentageType = "decimal(18,8)";

    public void Configure(EntityTypeBuilder<FundHolding> builder)
    {
        builder.ToTable("FundHoldings");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FundSnapshotId).IsRequired();
        builder.HasIndex(h => h.FundSnapshotId);

        builder.Property(h => h.SecurityId);
        builder.HasIndex(h => h.SecurityId);

        builder.Property(h => h.Weight).HasColumnType(PercentageType).IsRequired();
        builder.Property(h => h.FairValueUsd).HasColumnType(MoneyType);
        builder.Property(h => h.Balance).HasColumnType(MoneyType);
        builder.Property(h => h.Units).HasMaxLength(20);

        builder.Property(h => h.Name).HasMaxLength(500);
        builder.Property(h => h.Ticker).HasMaxLength(20);
        builder.Property(h => h.Isin).HasMaxLength(20);
        builder.Property(h => h.AssetCategory).HasMaxLength(20);
        builder.Property(h => h.Country).HasMaxLength(2);
        builder.Property(h => h.Currency).HasMaxLength(3);
    }
}
