using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Reference;
using Vizfolio.Infrastructure.Persistence.Seed;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class AssetCategoryConfiguration : IEntityTypeConfiguration<AssetCategory>
{
    public void Configure(EntityTypeBuilder<AssetCategory> builder)
    {
        builder.ToTable("AssetCategory");
        builder.HasKey(c => c.Code);

        builder.Property(c => c.Code).HasMaxLength(10).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasData(AssetCategorySeed.All
            .Select(c => new { c.Code, c.Name, c.Description }));
    }
}
