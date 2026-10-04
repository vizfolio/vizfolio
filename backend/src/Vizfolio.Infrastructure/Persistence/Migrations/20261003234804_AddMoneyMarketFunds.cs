using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyMarketFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoneyMarketFund",
                columns: table => new
                {
                    MoneyMarketFundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RegistrantCik = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SeeksStablePrice = table.Column<bool>(type: "INTEGER", nullable: false),
                    StablePricePerShare = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    IsRetail = table.Column<bool>(type: "INTEGER", nullable: true),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SourceFiling = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Tickers = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyMarketFund", x => x.MoneyMarketFundId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyMarketFund_SeriesId",
                table: "MoneyMarketFund",
                column: "SeriesId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoneyMarketFund");
        }
    }
}
