using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Institution = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_Account_Portfolio_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolio",
                        principalColumn: "PortfolioId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountTransaction",
                columns: table => new
                {
                    AccountTransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Cusip = table.Column<string>(type: "TEXT", maxLength: 9, nullable: true),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(28,4)", nullable: false),
                    Fees = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTransaction", x => x.AccountTransactionId);
                    table.ForeignKey(
                        name: "FK_AccountTransaction_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountTransaction_Currency_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountTransaction_Security_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Security",
                        principalColumn: "SecurityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_PortfolioId_Institution_AccountNumber",
                table: "Account",
                columns: new[] { "PortfolioId", "Institution", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_AccountId_SourceSystem_ExternalId",
                table: "AccountTransaction",
                columns: new[] { "AccountId", "SourceSystem", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_AccountId_TradeDate",
                table: "AccountTransaction",
                columns: new[] { "AccountId", "TradeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_CurrencyCode",
                table: "AccountTransaction",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_SecurityId",
                table: "AccountTransaction",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_Ticker",
                table: "AccountTransaction",
                column: "Ticker");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountTransaction");

            migrationBuilder.DropTable(
                name: "Account");
        }
    }
}
