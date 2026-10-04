using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "AccountTransaction",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSettlementFund",
                table: "AccountTransaction",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SplitDenominator",
                table: "AccountTransaction",
                type: "decimal(28,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SplitNumerator",
                table: "AccountTransaction",
                type: "decimal(28,8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubAccount",
                table: "AccountTransaction",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "AccountHoldingSnapshot",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriceAsOf",
                table: "AccountHoldingSnapshot",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByImportBatchId",
                table: "AccountHolding",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSettlementFund",
                table: "AccountHolding",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByImportBatchId",
                table: "Account",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportBatch",
                columns: table => new
                {
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    FileSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ParserSourceSystem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReprocessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    UndoneAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatch", x => x.ImportBatchId);
                    table.ForeignKey(
                        name: "FK_ImportBatch_Portfolio_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolio",
                        principalColumn: "PortfolioId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatchRowUpdate",
                columns: table => new
                {
                    ImportBatchRowUpdateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountTransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NextAmount = table.Column<decimal>(type: "decimal(28,4)", nullable: false),
                    NextIsSettlementFund = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextPrice = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    NextQuantity = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    NextSettlementDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NextSourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    NextType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "decimal(28,4)", nullable: false),
                    PreviousIsSettlementFund = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviousPrice = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    PreviousQuantity = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    PreviousSettlementDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PreviousSourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PreviousType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatchRowUpdate", x => x.ImportBatchRowUpdateId);
                    table.ForeignKey(
                        name: "FK_ImportBatchRowUpdate_ImportBatch_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatch",
                        principalColumn: "ImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_ImportBatchId",
                table: "AccountTransaction",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountHoldingSnapshot_ImportBatchId",
                table: "AccountHoldingSnapshot",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatch_FileSha256",
                table: "ImportBatch",
                column: "FileSha256");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatch_PortfolioId_ImportedAt",
                table: "ImportBatch",
                columns: new[] { "PortfolioId", "ImportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRowUpdate_AccountTransactionId",
                table: "ImportBatchRowUpdate",
                column: "AccountTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRowUpdate_ImportBatchId",
                table: "ImportBatchRowUpdate",
                column: "ImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportBatchRowUpdate");

            migrationBuilder.DropTable(
                name: "ImportBatch");

            migrationBuilder.DropIndex(
                name: "IX_AccountTransaction_ImportBatchId",
                table: "AccountTransaction");

            migrationBuilder.DropIndex(
                name: "IX_AccountHoldingSnapshot_ImportBatchId",
                table: "AccountHoldingSnapshot");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "IsSettlementFund",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "SplitDenominator",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "SplitNumerator",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "SubAccount",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "AccountHoldingSnapshot");

            migrationBuilder.DropColumn(
                name: "PriceAsOf",
                table: "AccountHoldingSnapshot");

            migrationBuilder.DropColumn(
                name: "CreatedByImportBatchId",
                table: "AccountHolding");

            migrationBuilder.DropColumn(
                name: "IsSettlementFund",
                table: "AccountHolding");

            migrationBuilder.DropColumn(
                name: "CreatedByImportBatchId",
                table: "Account");
        }
    }
}
