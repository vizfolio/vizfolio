using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("Funds");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.SeriesId).IsRequired().HasMaxLength(20);
        builder.HasIndex(f => f.SeriesId).IsUnique();

        builder.Property(f => f.Name).HasMaxLength(500);
        builder.Property(f => f.RegistrantCik).HasMaxLength(10);
        builder.Property(f => f.RegistrantName).HasMaxLength(500);
    }
}
