using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatch");
        builder.HasKey(b => b.ImportBatchId);

        builder.Property(b => b.PortfolioId).IsRequired();
        builder.HasOne<Portfolio>()
            .WithMany()
            .HasForeignKey(b => b.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        // The account an account-scoped upload went to. No FK: an undone import may have removed the account, and
        // the batch stays as a record of what happened.
        builder.Property(b => b.AccountId);

        builder.Property(b => b.FileName).IsRequired().HasMaxLength(260);
        builder.Property(b => b.FileSha256).IsRequired().HasMaxLength(64);
        // Not unique: the same file can go into different portfolios, and be imported again once undone.
        builder.HasIndex(b => b.FileSha256);

        builder.Property(b => b.ParserSourceSystem).IsRequired().HasMaxLength(20);
        builder.Property(b => b.ImportedAt).IsRequired();
        builder.Property(b => b.ReprocessedAt);
        builder.Property(b => b.Content);
        builder.Property(b => b.ContentType).HasMaxLength(100);
        builder.Property(b => b.SummaryJson);

        builder.Property(b => b.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);
        builder.Property(b => b.UndoneAt);

        builder.Ignore(b => b.IsUndone);

        builder.HasIndex(b => new { b.PortfolioId, b.ImportedAt });
    }
}
