using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Holdings;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    private const string MoneyType = "decimal(28,4)";
    private const string PercentageType = "decimal(18,8)";

    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable("Holdings");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FundSnapshotId).IsRequired();
        builder.HasIndex(h => h.FundSnapshotId);

        builder.Property(h => h.SecurityId);
        builder.HasIndex(h => h.SecurityId);

        builder.Property(h => h.CitSubstitutionId);
        builder.HasIndex(h => h.CitSubstitutionId);
        builder.HasOne<Vizfolio.Domain.Funds.CitSubstitution>()
            .WithMany()
            .HasForeignKey(h => h.CitSubstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

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
