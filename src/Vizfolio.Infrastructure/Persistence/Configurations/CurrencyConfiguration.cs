using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Reference;
using Vizfolio.Infrastructure.Persistence.Seed;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currency");
        builder.HasKey(c => c.Code);

        builder.Property(c => c.Code).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.MinorUnit).IsRequired();
        builder.Property(c => c.Symbol).HasMaxLength(10);

        builder.HasData(CurrencySeed.All
            .Select(c => new { c.Code, c.Name, c.MinorUnit, c.Symbol }));
    }
}
