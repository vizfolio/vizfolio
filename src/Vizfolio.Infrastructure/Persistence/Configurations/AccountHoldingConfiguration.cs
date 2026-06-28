using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class AccountHoldingConfiguration : IEntityTypeConfiguration<AccountHolding>
{
    public void Configure(EntityTypeBuilder<AccountHolding> builder)
    {
        builder.ToTable("AccountHolding");
        builder.HasKey(h => h.AccountHoldingId);

        builder.Property(h => h.AccountId).IsRequired();

        builder.Property(h => h.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(h => h.Symbol).HasMaxLength(32);
        builder.HasIndex(h => h.Symbol);

        builder.Property(h => h.Name).HasMaxLength(500);
        builder.Property(h => h.Isin).HasMaxLength(20);
        builder.Property(h => h.Cusip).HasMaxLength(9);

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

        builder.Property(h => h.CurrencyCode).HasMaxLength(3);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(h => h.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.SecurityId);
        builder.HasOne<Security>()
            .WithMany()
            .HasForeignKey(h => h.SecurityId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.FundId);
        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(h => h.FundId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(h => h.CreatedAt).IsRequired();

        builder.HasIndex(h => h.AccountId);
        builder.HasIndex(h => h.SecurityId);
        builder.HasIndex(h => h.FundId);
    }
}
