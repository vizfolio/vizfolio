using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceSeriesStatusAndAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSetting",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSetting", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PriceSeriesStatus",
                columns: table => new
                {
                    PriceSeriesStatusId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SymbolKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    QuerySymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSource = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LastOutcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    NeededFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FirstStored = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastStored = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NoDataBefore = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSeriesStatus", x => x.PriceSeriesStatusId);
                    table.ForeignKey(
                        name: "FK_PriceSeriesStatus_Security_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Security",
                        principalColumn: "SecurityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceSeriesStatus_SecurityId",
                table: "PriceSeriesStatus",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceSeriesStatus_SymbolKey",
                table: "PriceSeriesStatus",
                column: "SymbolKey");

            // Stooq's closes are split/dividend adjusted: mark the rows already stored so valuation ignores them (it only
            // uses as-traded closes). Quoted identifiers work on SQLite, Postgres and SQL Server; booleans differ.
            var adjusted = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "TRUE" : "1";
            migrationBuilder.Sql($"UPDATE \"PriceHistory\" SET \"Adjusted\" = {adjusted} WHERE \"Source\" = 'Stooq';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSetting");

            migrationBuilder.DropTable(
                name: "PriceSeriesStatus");
        }
    }
}
