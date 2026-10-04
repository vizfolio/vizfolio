using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class MoneyMarketFundConfiguration : IEntityTypeConfiguration<MoneyMarketFund>
{
    public void Configure(EntityTypeBuilder<MoneyMarketFund> builder)
    {
        builder.ToTable("MoneyMarketFund");
        builder.HasKey(f => f.MoneyMarketFundId);

        builder.Property(f => f.SeriesId).IsRequired().HasMaxLength(20);
        builder.HasIndex(f => f.SeriesId).IsUnique();

        builder.Property(f => f.Name).HasMaxLength(500);
        builder.Property(f => f.RegistrantCik).HasMaxLength(10);
        builder.Property(f => f.Category).HasMaxLength(100);
        builder.Property(f => f.StablePricePerShare).HasPrecision(18, 6);
        builder.Property(f => f.SourceFiling).IsRequired().HasMaxLength(30);

        builder.PrimitiveCollection<List<string>>("_tickers")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_tickers")
            .HasColumnName("Tickers");

        builder.Ignore(f => f.Tickers);
        builder.Ignore(f => f.StablePrice);
    }
}
