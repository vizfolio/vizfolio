using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Reference;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class AccountHoldingSnapshotConfiguration : IEntityTypeConfiguration<AccountHoldingSnapshot>
{
    private const string MoneyType = "decimal(28,4)";
    private const string SharesType = "decimal(28,8)";

    public void Configure(EntityTypeBuilder<AccountHoldingSnapshot> builder)
    {
        builder.ToTable("AccountHoldingSnapshot");
        builder.HasKey(s => s.AccountHoldingSnapshotId);

        builder.Property(s => s.AccountHoldingId).IsRequired();
        builder.HasOne<AccountHolding>()
            .WithMany()
            .HasForeignKey(s => s.AccountHoldingId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Property(s => s.AsOf).IsRequired();

        builder.Property(s => s.Quantity).HasColumnType(SharesType).IsRequired();
        builder.Property(s => s.CostBasis).HasColumnType(MoneyType);
        builder.Property(s => s.MarketValue).HasColumnType(MoneyType);
        builder.Property(s => s.UnitPrice).HasColumnType(SharesType);

        builder.Property(s => s.CurrencyCode).HasMaxLength(3);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(s => s.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(s => s.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.RecordedAt).IsRequired();

        builder.HasIndex(s => new { s.AccountHoldingId, s.AsOf }).IsUnique();
    }
}
