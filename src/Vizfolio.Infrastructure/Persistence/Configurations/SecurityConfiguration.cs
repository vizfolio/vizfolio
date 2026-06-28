using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Securities;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class SecurityConfiguration : IEntityTypeConfiguration<Security>
{
    public void Configure(EntityTypeBuilder<Security> builder)
    {
        builder.ToTable("Security");
        builder.HasKey(s => s.SecurityId);

        builder.Property(s => s.Cik).IsRequired().HasMaxLength(10);
        builder.HasIndex(s => s.Cik).IsUnique();

        builder.Property(s => s.Name).HasMaxLength(500);
        builder.Property(s => s.EntityType).HasMaxLength(100);
        builder.Property(s => s.Sector).HasMaxLength(200);
        builder.Property(s => s.Industry).HasMaxLength(200);
        builder.Property(s => s.Country).HasMaxLength(2);
        builder.Property(s => s.EdgarFetchedAt).IsRequired();

        builder.PrimitiveCollection<List<string>>("_tickers")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_tickers")
            .HasColumnName("Tickers");

        builder.Ignore(s => s.Tickers);

        builder.PrimitiveCollection<List<string>>("_exchanges")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_exchanges")
            .HasColumnName("Exchanges");

        builder.Ignore(s => s.Exchanges);
    }
}
