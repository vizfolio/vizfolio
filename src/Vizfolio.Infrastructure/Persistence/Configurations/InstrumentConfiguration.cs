using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;
using Vizfolio.Domain.Instruments;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("Instrument");
        builder.HasKey(i => i.InstrumentId);

        builder.Property(i => i.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(i => i.Symbol).HasMaxLength(32);
        builder.HasIndex(i => i.Symbol);

        builder.Property(i => i.Name).HasMaxLength(500);
        builder.Property(i => i.Isin).HasMaxLength(20);
        builder.Property(i => i.Cusip).HasMaxLength(9);

        builder.Property(i => i.AssetCategoryCode).HasMaxLength(10);
        builder.HasOne<AssetCategory>()
            .WithMany()
            .HasForeignKey(i => i.AssetCategoryCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(i => i.AssetClassCode).HasMaxLength(20);
        builder.HasOne<AssetClass>()
            .WithMany()
            .HasForeignKey(i => i.AssetClassCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(i => i.CurrencyCode).HasMaxLength(3);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(i => i.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(i => i.SecurityId);
        builder.HasOne<Security>()
            .WithMany()
            .HasForeignKey(i => i.SecurityId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(i => i.FundId);
        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(i => i.FundId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasIndex(i => i.SecurityId);
        builder.HasIndex(i => i.FundId);
    }
}
