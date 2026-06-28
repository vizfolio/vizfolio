using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Account");
        builder.HasKey(a => a.AccountId);

        builder.Property(a => a.PortfolioId).IsRequired();
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Institution).IsRequired().HasMaxLength(100);
        builder.Property(a => a.AccountNumber).IsRequired().HasMaxLength(50);
        builder.Property(a => a.AccountType).HasMaxLength(50);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => new { a.PortfolioId, a.Institution, a.AccountNumber }).IsUnique();

        builder.HasOne<Portfolio>()
            .WithMany(p => p.Accounts)
            .HasForeignKey(a => a.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Transactions)
            .WithOne()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
