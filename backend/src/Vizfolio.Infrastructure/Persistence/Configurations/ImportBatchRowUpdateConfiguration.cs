using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class ImportBatchRowUpdateConfiguration : IEntityTypeConfiguration<ImportBatchRowUpdate>
{
    private const string MoneyType = "decimal(28,4)";
    private const string SharesType = "decimal(28,8)";

    public void Configure(EntityTypeBuilder<ImportBatchRowUpdate> builder)
    {
        builder.ToTable("ImportBatchRowUpdate");
        builder.HasKey(u => u.ImportBatchRowUpdateId);

        builder.Property(u => u.ImportBatchId).IsRequired();
        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(u => u.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK to the transaction: it cascades from the portfolio through its account as this row does through its
        // batch, and SQL Server rejects two cascade paths. Undo removes updates of the rows it deletes itself.
        builder.Property(u => u.AccountTransactionId).IsRequired();

        builder.ComplexProperty(u => u.Previous, f => ConfigureFields(f, "Previous"));
        builder.ComplexProperty(u => u.Next, f => ConfigureFields(f, "Next"));

        builder.Property(u => u.RecordedAt).IsRequired();
        builder.HasIndex(u => u.ImportBatchId);
        builder.HasIndex(u => u.AccountTransactionId);
    }

    private static void ConfigureFields(ComplexPropertyBuilder<ImportedTransactionFields> fields, string prefix)
    {
        fields.Property(f => f.Type).HasColumnName($"{prefix}Type").IsRequired().HasConversion<string>().HasMaxLength(20);
        fields.Property(f => f.Amount).HasColumnName($"{prefix}Amount").HasColumnType(MoneyType).IsRequired();
        fields.Property(f => f.Quantity).HasColumnName($"{prefix}Quantity").HasColumnType(SharesType);
        fields.Property(f => f.Price).HasColumnName($"{prefix}Price").HasColumnType(SharesType);
        fields.Property(f => f.SettlementDate).HasColumnName($"{prefix}SettlementDate");
        fields.Property(f => f.SourceType).HasColumnName($"{prefix}SourceType").HasMaxLength(50);
        fields.Property(f => f.IsSettlementFund).HasColumnName($"{prefix}IsSettlementFund").IsRequired();
    }
}
