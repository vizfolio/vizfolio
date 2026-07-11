using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Reference;
using Vizfolio.Infrastructure.Persistence.Seed;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Country");
        builder.HasKey(c => c.Code);

        builder.Property(c => c.Code).HasMaxLength(2).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Region).HasMaxLength(50);

        builder.HasData(CountrySeed.All
            .Select(c => new { c.Code, c.Name, c.Region }));
    }
}
