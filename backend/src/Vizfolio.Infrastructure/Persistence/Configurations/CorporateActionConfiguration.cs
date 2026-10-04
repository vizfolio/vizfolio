using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    private const string FactorType = "decimal(28,8)";

    public void Configure(EntityTypeBuilder<CorporateAction> builder)
    {
        builder.ToTable("CorporateAction");
        builder.HasKey(c => c.CorporateActionId);

        builder.Property(c => c.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(c => c.SecurityId);
        builder.HasOne<Security>()
            .WithMany()
            .HasForeignKey(c => c.SecurityId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(c => c.SymbolKey).HasMaxLength(64);

        builder.Property(c => c.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.ExDate).IsRequired();
        builder.Property(c => c.SplitNumerator).HasColumnType(FactorType).IsRequired();
        builder.Property(c => c.SplitDenominator).HasColumnType(FactorType).IsRequired();

        builder.Ignore(c => c.SplitFactor);

        builder.Property(c => c.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.FetchedAt).IsRequired();

        // Dedupe (one action per series per ex-date) is enforced in the importer, same reasoning as
        // PriceHistoryConfiguration.
        builder.HasIndex(c => new { c.SecurityId, c.ExDate });
        builder.HasIndex(c => new { c.SymbolKey, c.ExDate });
    }
}
