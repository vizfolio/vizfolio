using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuerCikToFundHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssuerCik",
                table: "FundHolding",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_IssuerCik",
                table: "FundHolding",
                column: "IssuerCik");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FundHolding_IssuerCik",
                table: "FundHolding");

            migrationBuilder.DropColumn(
                name: "IssuerCik",
                table: "FundHolding");
        }
    }
}
