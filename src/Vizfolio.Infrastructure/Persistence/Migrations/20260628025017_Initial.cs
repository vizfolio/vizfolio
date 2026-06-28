using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CitSubstitution",
                columns: table => new
                {
                    CitSubstitutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubstituteTicker = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubstituteName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Fidelity = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CitName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Patterns = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitSubstitution", x => x.CitSubstitutionId);
                });

            migrationBuilder.CreateTable(
                name: "Fund",
                columns: table => new
                {
                    FundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RegistrantCik = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    RegistrantName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fund", x => x.FundId);
                });

            migrationBuilder.CreateTable(
                name: "Portfolio",
                columns: table => new
                {
                    PortfolioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolio", x => x.PortfolioId);
                });

            migrationBuilder.CreateTable(
                name: "Security",
                columns: table => new
                {
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Cik = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Sector = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    EdgarFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Exchanges = table.Column<string>(type: "TEXT", nullable: false),
                    Tickers = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security", x => x.SecurityId);
                });

            migrationBuilder.CreateTable(
                name: "FundSnapshot",
                columns: table => new
                {
                    FundSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SourceFiling = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NetAssetsUsd = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    TotalAssetsUsd = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    TotalLiabilitiesUsd = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    CashNotInPortfolioUsd = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    IsFinalFiling = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundSnapshot", x => x.FundSnapshotId);
                    table.ForeignKey(
                        name: "FK_FundSnapshot_Fund_FundId",
                        column: x => x.FundId,
                        principalTable: "Fund",
                        principalColumn: "FundId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FundHolding",
                columns: table => new
                {
                    FundHoldingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FundSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    FairValueUsd = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(28,4)", nullable: true),
                    Units = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Isin = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    AssetCategory = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundHolding", x => x.FundHoldingId);
                    table.ForeignKey(
                        name: "FK_FundHolding_FundSnapshot_FundSnapshotId",
                        column: x => x.FundSnapshotId,
                        principalTable: "FundSnapshot",
                        principalColumn: "FundSnapshotId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FundHolding_Security_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Security",
                        principalColumn: "SecurityId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FundMonthlyReturn",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReturnPct = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    ClassId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    FundSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundMonthlyReturn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundMonthlyReturn_FundSnapshot_FundSnapshotId",
                        column: x => x.FundSnapshotId,
                        principalTable: "FundSnapshot",
                        principalColumn: "FundSnapshotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FundShareClass",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClassId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    ExpenseRatio = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    FundSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundShareClass", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundShareClass_FundSnapshot_FundSnapshotId",
                        column: x => x.FundSnapshotId,
                        principalTable: "FundSnapshot",
                        principalColumn: "FundSnapshotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitSubstitution_SubstituteTicker",
                table: "CitSubstitution",
                column: "SubstituteTicker");

            migrationBuilder.CreateIndex(
                name: "IX_Fund_SeriesId",
                table: "Fund",
                column: "SeriesId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_FundSnapshotId",
                table: "FundHolding",
                column: "FundSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_SecurityId",
                table: "FundHolding",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_FundMonthlyReturn_FundSnapshotId_Month_ClassId",
                table: "FundMonthlyReturn",
                columns: new[] { "FundSnapshotId", "Month", "ClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundShareClass_FundSnapshotId_ClassId",
                table: "FundShareClass",
                columns: new[] { "FundSnapshotId", "ClassId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundSnapshot_FundId_AsOf",
                table: "FundSnapshot",
                columns: new[] { "FundId", "AsOf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Security_Cik",
                table: "Security",
                column: "Cik",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitSubstitution");

            migrationBuilder.DropTable(
                name: "FundHolding");

            migrationBuilder.DropTable(
                name: "FundMonthlyReturn");

            migrationBuilder.DropTable(
                name: "FundShareClass");

            migrationBuilder.DropTable(
                name: "Portfolio");

            migrationBuilder.DropTable(
                name: "Security");

            migrationBuilder.DropTable(
                name: "FundSnapshot");

            migrationBuilder.DropTable(
                name: "Fund");
        }
    }
}
