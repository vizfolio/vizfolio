using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Reference;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class PriceHistoryConfiguration : IEntityTypeConfiguration<PriceHistory>
{
    private const string MoneyType = "decimal(28,4)";

    public void Configure(EntityTypeBuilder<PriceHistory> builder)
    {
        builder.ToTable("PriceHistory");
        builder.HasKey(p => p.PriceHistoryId);

        builder.Property(p => p.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(p => p.SecurityId);
        builder.HasOne<Security>()
            .WithMany()
            .HasForeignKey(p => p.SecurityId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(p => p.SymbolKey).HasMaxLength(64);

        builder.Property(p => p.AsOf).IsRequired();
        builder.Property(p => p.Close).HasColumnType(MoneyType).IsRequired();

        builder.Property(p => p.CurrencyCode).HasMaxLength(3);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(p => p.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(p => p.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.Adjusted).IsRequired();
        builder.Property(p => p.FetchedAt).IsRequired();

        // Not marked unique: a filtered/partial unique index over the exactly-one-of-(SecurityId,
        // SymbolKey) series key isn't portable across providers (null handling differs). One row per
        // series per day is enforced in PriceHistoryImporter's in-memory upsert.
        builder.HasIndex(p => new { p.SecurityId, p.AsOf });
        builder.HasIndex(p => new { p.SymbolKey, p.AsOf });
        builder.HasIndex(p => p.AsOf);
    }
}
