using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Funds;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class CitSubstitutionConfiguration : IEntityTypeConfiguration<CitSubstitution>
{
    public void Configure(EntityTypeBuilder<CitSubstitution> builder)
    {
        builder.ToTable("CitSubstitutions");
        builder.HasKey(c => c.CitSubstitutionId);

        builder.Property(c => c.SubstituteTicker).IsRequired().HasMaxLength(20);
        builder.HasIndex(c => c.SubstituteTicker);

        builder.Property(c => c.SubstituteName).IsRequired().HasMaxLength(500);

        builder.Property(c => c.Fidelity)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(c => c.Note).HasMaxLength(2000);
        builder.Property(c => c.CitName).HasMaxLength(500);

        builder.PrimitiveCollection<List<string>>("_patterns")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_patterns");

        builder.Ignore(c => c.Patterns);
    }
}
