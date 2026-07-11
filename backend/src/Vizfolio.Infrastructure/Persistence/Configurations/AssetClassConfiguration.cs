using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Reference;
using Vizfolio.Infrastructure.Persistence.Seed;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class AssetClassConfiguration : IEntityTypeConfiguration<AssetClass>
{
    public void Configure(EntityTypeBuilder<AssetClass> builder)
    {
        builder.ToTable("AssetClass");
        builder.HasKey(c => c.Code);

        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasData(AssetClassSeed.All
            .Select(c => new { c.Code, c.Name, c.Description }));
    }
}
