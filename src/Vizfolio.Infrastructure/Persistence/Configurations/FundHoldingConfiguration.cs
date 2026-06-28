using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class FundHoldingConfiguration : IEntityTypeConfiguration<FundHolding>
{
    private const string MoneyType = "decimal(28,4)";
    private const string PercentageType = "decimal(18,8)";

    public void Configure(EntityTypeBuilder<FundHolding> builder)
    {
        builder.ToTable("FundHolding");
        builder.HasKey(h => h.FundHoldingId);

        builder.Property(h => h.FundSnapshotId).IsRequired();

        builder.HasOne<FundSnapshot>()
            .WithMany()
            .HasForeignKey(h => h.FundSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(h => h.SecurityId);
        builder.HasOne<Security>()
            .WithMany()
            .HasForeignKey(h => h.SecurityId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

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
