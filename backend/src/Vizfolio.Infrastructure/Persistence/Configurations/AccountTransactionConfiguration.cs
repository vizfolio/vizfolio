using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Reference;

namespace Vizfolio.Infrastructure.Persistence.Configurations;

internal sealed class AccountTransactionConfiguration : IEntityTypeConfiguration<AccountTransaction>
{
    private const string MoneyType = "decimal(28,4)";
    private const string SharesType = "decimal(28,8)";

    public void Configure(EntityTypeBuilder<AccountTransaction> builder)
    {
        builder.ToTable("AccountTransaction");
        builder.HasKey(t => t.AccountTransactionId);

        builder.Property(t => t.AccountId).IsRequired();
        builder.Property(t => t.SourceSystem).IsRequired().HasMaxLength(20);
        builder.Property(t => t.ExternalId).IsRequired().HasMaxLength(128);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.TradeDate).IsRequired();
        builder.Property(t => t.SettlementDate);

        builder.Property(t => t.Ticker).HasMaxLength(20);
        builder.Property(t => t.Cusip).HasMaxLength(9);

        builder.Property(t => t.AccountHoldingId);
        builder.HasOne<AccountHolding>()
            .WithMany()
            .HasForeignKey(t => t.AccountHoldingId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(t => t.Quantity).HasColumnType(SharesType);
        builder.Property(t => t.Price).HasColumnType(SharesType);
        builder.Property(t => t.Amount).HasColumnType(MoneyType).IsRequired();
        builder.Property(t => t.Fees).HasColumnType(MoneyType);

        builder.Property(t => t.CurrencyCode).HasMaxLength(3);
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(t => t.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(t => t.Memo).HasMaxLength(500);
        builder.Property(t => t.SourceType).HasMaxLength(50);
        builder.Property(t => t.ImportedAt).IsRequired();

        // The upload that inserted the row: a provenance pointer with no FK. Both the row (via its account) and the
        // batch cascade from the portfolio, and SQL Server rejects a second cascade/set-null path. Batches are never
        // deleted (undo marks them), so the pointer can't dangle.
        builder.Property(t => t.ImportBatchId);
        builder.HasIndex(t => t.ImportBatchId);

        builder.Property(t => t.SplitNumerator).HasColumnType(SharesType);
        builder.Property(t => t.SplitDenominator).HasColumnType(SharesType);
        builder.Ignore(t => t.SplitFactor);
        builder.Property(t => t.IsSettlementFund).IsRequired().HasDefaultValue(false);
        builder.Property(t => t.SubAccount).HasMaxLength(40);

        builder.HasIndex(t => new { t.AccountId, t.SourceSystem, t.ExternalId }).IsUnique();
        builder.HasIndex(t => new { t.AccountId, t.TradeDate });
        builder.HasIndex(t => t.Ticker);
        builder.HasIndex(t => t.AccountHoldingId);
    }
}
