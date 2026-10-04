using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Pricing;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class PriceSeriesStatusConfiguration : IEntityTypeConfiguration<PriceSeriesStatus>
{
    public void Configure(EntityTypeBuilder<PriceSeriesStatus> builder)
    {
        builder.ToTable("PriceSeriesStatus");
        builder.HasKey(s => s.PriceSeriesStatusId);

        builder.Property(s => s.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.SecurityId);
        builder.HasOne<Security>()
            .WithMany()
            .HasForeignKey(s => s.SecurityId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        builder.Property(s => s.SymbolKey).HasMaxLength(64);
        builder.Property(s => s.QuerySymbol).IsRequired().HasMaxLength(64);

        builder.Property(s => s.LastAttemptAt);
        builder.Property(s => s.LastSource).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.LastOutcome).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Message).HasMaxLength(500);
        builder.Property(s => s.NeededFrom);
        builder.Property(s => s.FirstStored);
        builder.Property(s => s.LastStored);
        builder.Property(s => s.NoDataBefore);

        // One row per series, enforced by PriceHistoryImporter (as for PriceHistory, a filtered unique index over the
        // exactly-one-of key isn't portable).
        builder.HasIndex(s => s.SecurityId);
        builder.HasIndex(s => s.SymbolKey);
    }
}
