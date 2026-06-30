using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Reference;
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

        builder.Property(h => h.AssetCategoryCode).HasMaxLength(10);
        builder.HasOne<AssetCategory>()
            .WithMany()
            .HasForeignKey(h => h.AssetCategoryCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.AssetClassCode).HasMaxLength(20);
        builder.HasOne<AssetClass>()
            .WithMany()
            .HasForeignKey(h => h.AssetClassCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.CountryCode).HasMaxLength(2);
        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(h => h.CountryCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.CurrencyCode).HasMaxLength(3);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(h => h.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.IssuerCik).HasMaxLength(10);
        builder.HasIndex(h => h.IssuerCik);
    }
}
