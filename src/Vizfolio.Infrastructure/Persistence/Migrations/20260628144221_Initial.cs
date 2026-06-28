using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vizfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetCategory",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategory", x => x.Code);
                });

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
                name: "Country",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Country", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Currency",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MinorUnit = table.Column<int>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency", x => x.Code);
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
                    Industry = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    EdgarFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Exchanges = table.Column<string>(type: "TEXT", nullable: false),
                    Tickers = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security", x => x.SecurityId);
                    table.ForeignKey(
                        name: "FK_Security_Country_CountryCode",
                        column: x => x.CountryCode,
                        principalTable: "Country",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
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
                    AssetCategoryCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    IssuerCik = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundHolding", x => x.FundHoldingId);
                    table.ForeignKey(
                        name: "FK_FundHolding_AssetCategory_AssetCategoryCode",
                        column: x => x.AssetCategoryCode,
                        principalTable: "AssetCategory",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundHolding_Country_CountryCode",
                        column: x => x.CountryCode,
                        principalTable: "Country",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundHolding_Currency_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.InsertData(
                table: "AssetCategory",
                columns: new[] { "Code", "Description", "Name" },
                values: new object[,]
                {
                    { "ABS-APCP", "Asset-backed commercial paper.", "ABS-Asset Backed Commercial Paper" },
                    { "ABS-CB", "Collateralized bond or debt obligation.", "ABS-Collateralized Bond/Debt Obligation" },
                    { "ABS-MBS", "Mortgage-backed security.", "ABS-Mortgage Backed Security" },
                    { "ABS-O", "Other asset-backed security not otherwise classified.", "ABS-Other" },
                    { "COMM", "Physical commodity or commodity-linked instrument.", "Commodity" },
                    { "DBT", "Debt securities.", "Debt" },
                    { "DCO", "Derivative referencing a commodity.", "Derivative-Commodity" },
                    { "DCR", "Derivative referencing credit (e.g. credit default swap).", "Derivative-Credit" },
                    { "DE", "Derivative referencing equity.", "Derivative-Equity" },
                    { "DFE", "Derivative referencing foreign exchange.", "Derivative-Foreign Exchange" },
                    { "DIR", "Derivative referencing interest rates.", "Derivative-Interest Rate" },
                    { "DO", "Derivative not otherwise classified.", "Derivative-Other" },
                    { "EC", "Common equity securities.", "Equity-Common" },
                    { "EP", "Preferred equity securities.", "Equity-Preferred" },
                    { "LON", "Loans and loan participations.", "Loan" },
                    { "RA", "Repurchase or reverse repurchase agreement.", "Repurchase Agreement" },
                    { "RE", "Direct real estate or real estate-linked investment.", "Real Estate" },
                    { "STIV", "Money market fund, liquidity pool, or other cash management vehicle.", "Short-Term Investment Vehicle" }
                });

            migrationBuilder.InsertData(
                table: "Country",
                columns: new[] { "Code", "Name", "Region" },
                values: new object[,]
                {
                    { "AD", "Andorra", "Europe" },
                    { "AE", "United Arab Emirates", "Asia" },
                    { "AF", "Afghanistan", "Asia" },
                    { "AG", "Antigua and Barbuda", "Americas" },
                    { "AI", "Anguilla", "Americas" },
                    { "AL", "Albania", "Europe" },
                    { "AM", "Armenia", "Asia" },
                    { "AO", "Angola", "Africa" },
                    { "AQ", "Antarctica", "Antarctic" },
                    { "AR", "Argentina", "Americas" },
                    { "AS", "American Samoa", "Oceania" },
                    { "AT", "Austria", "Europe" },
                    { "AU", "Australia", "Oceania" },
                    { "AW", "Aruba", "Americas" },
                    { "AX", "Åland Islands", "Europe" },
                    { "AZ", "Azerbaijan", "Asia" },
                    { "BA", "Bosnia and Herzegovina", "Europe" },
                    { "BB", "Barbados", "Americas" },
                    { "BD", "Bangladesh", "Asia" },
                    { "BE", "Belgium", "Europe" },
                    { "BF", "Burkina Faso", "Africa" },
                    { "BG", "Bulgaria", "Europe" },
                    { "BH", "Bahrain", "Asia" },
                    { "BI", "Burundi", "Africa" },
                    { "BJ", "Benin", "Africa" },
                    { "BL", "Saint Barthélemy", "Americas" },
                    { "BM", "Bermuda", "Americas" },
                    { "BN", "Brunei Darussalam", "Asia" },
                    { "BO", "Bolivia (Plurinational State of)", "Americas" },
                    { "BQ", "Bonaire, Sint Eustatius and Saba", "Americas" },
                    { "BR", "Brazil", "Americas" },
                    { "BS", "Bahamas", "Americas" },
                    { "BT", "Bhutan", "Asia" },
                    { "BV", "Bouvet Island", "Antarctic" },
                    { "BW", "Botswana", "Africa" },
                    { "BY", "Belarus", "Europe" },
                    { "BZ", "Belize", "Americas" },
                    { "CA", "Canada", "Americas" },
                    { "CC", "Cocos (Keeling) Islands", "Asia" },
                    { "CD", "Congo, Democratic Republic of the", "Africa" },
                    { "CF", "Central African Republic", "Africa" },
                    { "CG", "Congo", "Africa" },
                    { "CH", "Switzerland", "Europe" },
                    { "CI", "Côte d'Ivoire", "Africa" },
                    { "CK", "Cook Islands", "Oceania" },
                    { "CL", "Chile", "Americas" },
                    { "CM", "Cameroon", "Africa" },
                    { "CN", "China", "Asia" },
                    { "CO", "Colombia", "Americas" },
                    { "CR", "Costa Rica", "Americas" },
                    { "CU", "Cuba", "Americas" },
                    { "CV", "Cabo Verde", "Africa" },
                    { "CW", "Curaçao", "Americas" },
                    { "CX", "Christmas Island", "Oceania" },
                    { "CY", "Cyprus", "Asia" },
                    { "CZ", "Czechia", "Europe" },
                    { "DE", "Germany", "Europe" },
                    { "DJ", "Djibouti", "Africa" },
                    { "DK", "Denmark", "Europe" },
                    { "DM", "Dominica", "Americas" },
                    { "DO", "Dominican Republic", "Americas" },
                    { "DZ", "Algeria", "Africa" },
                    { "EC", "Ecuador", "Americas" },
                    { "EE", "Estonia", "Europe" },
                    { "EG", "Egypt", "Africa" },
                    { "EH", "Western Sahara", "Africa" },
                    { "ER", "Eritrea", "Africa" },
                    { "ES", "Spain", "Europe" },
                    { "ET", "Ethiopia", "Africa" },
                    { "FI", "Finland", "Europe" },
                    { "FJ", "Fiji", "Oceania" },
                    { "FK", "Falkland Islands (Malvinas)", "Americas" },
                    { "FM", "Micronesia, Federated States of", "Oceania" },
                    { "FO", "Faroe Islands", "Europe" },
                    { "FR", "France", "Europe" },
                    { "GA", "Gabon", "Africa" },
                    { "GB", "United Kingdom", "Europe" },
                    { "GD", "Grenada", "Americas" },
                    { "GE", "Georgia", "Asia" },
                    { "GF", "French Guiana", "Americas" },
                    { "GG", "Guernsey", "Europe" },
                    { "GH", "Ghana", "Africa" },
                    { "GI", "Gibraltar", "Europe" },
                    { "GL", "Greenland", "Americas" },
                    { "GM", "Gambia", "Africa" },
                    { "GN", "Guinea", "Africa" },
                    { "GP", "Guadeloupe", "Americas" },
                    { "GQ", "Equatorial Guinea", "Africa" },
                    { "GR", "Greece", "Europe" },
                    { "GS", "South Georgia and the South Sandwich Islands", "Americas" },
                    { "GT", "Guatemala", "Americas" },
                    { "GU", "Guam", "Oceania" },
                    { "GW", "Guinea-Bissau", "Africa" },
                    { "GY", "Guyana", "Americas" },
                    { "HK", "Hong Kong", "Asia" },
                    { "HM", "Heard Island and McDonald Islands", "Antarctic" },
                    { "HN", "Honduras", "Americas" },
                    { "HR", "Croatia", "Europe" },
                    { "HT", "Haiti", "Americas" },
                    { "HU", "Hungary", "Europe" },
                    { "ID", "Indonesia", "Asia" },
                    { "IE", "Ireland", "Europe" },
                    { "IL", "Israel", "Asia" },
                    { "IM", "Isle of Man", "Europe" },
                    { "IN", "India", "Asia" },
                    { "IO", "British Indian Ocean Territory", "Asia" },
                    { "IQ", "Iraq", "Asia" },
                    { "IR", "Iran (Islamic Republic of)", "Asia" },
                    { "IS", "Iceland", "Europe" },
                    { "IT", "Italy", "Europe" },
                    { "JE", "Jersey", "Europe" },
                    { "JM", "Jamaica", "Americas" },
                    { "JO", "Jordan", "Asia" },
                    { "JP", "Japan", "Asia" },
                    { "KE", "Kenya", "Africa" },
                    { "KG", "Kyrgyzstan", "Asia" },
                    { "KH", "Cambodia", "Asia" },
                    { "KI", "Kiribati", "Oceania" },
                    { "KM", "Comoros", "Africa" },
                    { "KN", "Saint Kitts and Nevis", "Americas" },
                    { "KP", "Korea (Democratic People's Republic of)", "Asia" },
                    { "KR", "Korea, Republic of", "Asia" },
                    { "KW", "Kuwait", "Asia" },
                    { "KY", "Cayman Islands", "Americas" },
                    { "KZ", "Kazakhstan", "Asia" },
                    { "LA", "Lao People's Democratic Republic", "Asia" },
                    { "LB", "Lebanon", "Asia" },
                    { "LC", "Saint Lucia", "Americas" },
                    { "LI", "Liechtenstein", "Europe" },
                    { "LK", "Sri Lanka", "Asia" },
                    { "LR", "Liberia", "Africa" },
                    { "LS", "Lesotho", "Africa" },
                    { "LT", "Lithuania", "Europe" },
                    { "LU", "Luxembourg", "Europe" },
                    { "LV", "Latvia", "Europe" },
                    { "LY", "Libya", "Africa" },
                    { "MA", "Morocco", "Africa" },
                    { "MC", "Monaco", "Europe" },
                    { "MD", "Moldova, Republic of", "Europe" },
                    { "ME", "Montenegro", "Europe" },
                    { "MF", "Saint Martin (French part)", "Americas" },
                    { "MG", "Madagascar", "Africa" },
                    { "MH", "Marshall Islands", "Oceania" },
                    { "MK", "North Macedonia", "Europe" },
                    { "ML", "Mali", "Africa" },
                    { "MM", "Myanmar", "Asia" },
                    { "MN", "Mongolia", "Asia" },
                    { "MO", "Macao", "Asia" },
                    { "MP", "Northern Mariana Islands", "Oceania" },
                    { "MQ", "Martinique", "Americas" },
                    { "MR", "Mauritania", "Africa" },
                    { "MS", "Montserrat", "Americas" },
                    { "MT", "Malta", "Europe" },
                    { "MU", "Mauritius", "Africa" },
                    { "MV", "Maldives", "Asia" },
                    { "MW", "Malawi", "Africa" },
                    { "MX", "Mexico", "Americas" },
                    { "MY", "Malaysia", "Asia" },
                    { "MZ", "Mozambique", "Africa" },
                    { "NA", "Namibia", "Africa" },
                    { "NC", "New Caledonia", "Oceania" },
                    { "NE", "Niger", "Africa" },
                    { "NF", "Norfolk Island", "Oceania" },
                    { "NG", "Nigeria", "Africa" },
                    { "NI", "Nicaragua", "Americas" },
                    { "NL", "Netherlands", "Europe" },
                    { "NO", "Norway", "Europe" },
                    { "NP", "Nepal", "Asia" },
                    { "NR", "Nauru", "Oceania" },
                    { "NU", "Niue", "Oceania" },
                    { "NZ", "New Zealand", "Oceania" },
                    { "OM", "Oman", "Asia" },
                    { "PA", "Panama", "Americas" },
                    { "PE", "Peru", "Americas" },
                    { "PF", "French Polynesia", "Oceania" },
                    { "PG", "Papua New Guinea", "Oceania" },
                    { "PH", "Philippines", "Asia" },
                    { "PK", "Pakistan", "Asia" },
                    { "PL", "Poland", "Europe" },
                    { "PM", "Saint Pierre and Miquelon", "Americas" },
                    { "PN", "Pitcairn", "Oceania" },
                    { "PR", "Puerto Rico", "Americas" },
                    { "PS", "Palestine, State of", "Asia" },
                    { "PT", "Portugal", "Europe" },
                    { "PW", "Palau", "Oceania" },
                    { "PY", "Paraguay", "Americas" },
                    { "QA", "Qatar", "Asia" },
                    { "RE", "Réunion", "Africa" },
                    { "RO", "Romania", "Europe" },
                    { "RS", "Serbia", "Europe" },
                    { "RU", "Russian Federation", "Europe" },
                    { "RW", "Rwanda", "Africa" },
                    { "SA", "Saudi Arabia", "Asia" },
                    { "SB", "Solomon Islands", "Oceania" },
                    { "SC", "Seychelles", "Africa" },
                    { "SD", "Sudan", "Africa" },
                    { "SE", "Sweden", "Europe" },
                    { "SG", "Singapore", "Asia" },
                    { "SH", "Saint Helena, Ascension and Tristan da Cunha", "Africa" },
                    { "SI", "Slovenia", "Europe" },
                    { "SJ", "Svalbard and Jan Mayen", "Europe" },
                    { "SK", "Slovakia", "Europe" },
                    { "SL", "Sierra Leone", "Africa" },
                    { "SM", "San Marino", "Europe" },
                    { "SN", "Senegal", "Africa" },
                    { "SO", "Somalia", "Africa" },
                    { "SR", "Suriname", "Americas" },
                    { "SS", "South Sudan", "Africa" },
                    { "ST", "Sao Tome and Principe", "Africa" },
                    { "SV", "El Salvador", "Americas" },
                    { "SX", "Sint Maarten (Dutch part)", "Americas" },
                    { "SY", "Syrian Arab Republic", "Asia" },
                    { "SZ", "Eswatini", "Africa" },
                    { "TC", "Turks and Caicos Islands", "Americas" },
                    { "TD", "Chad", "Africa" },
                    { "TF", "French Southern Territories", "Antarctic" },
                    { "TG", "Togo", "Africa" },
                    { "TH", "Thailand", "Asia" },
                    { "TJ", "Tajikistan", "Asia" },
                    { "TK", "Tokelau", "Oceania" },
                    { "TL", "Timor-Leste", "Asia" },
                    { "TM", "Turkmenistan", "Asia" },
                    { "TN", "Tunisia", "Africa" },
                    { "TO", "Tonga", "Oceania" },
                    { "TR", "Türkiye", "Asia" },
                    { "TT", "Trinidad and Tobago", "Americas" },
                    { "TV", "Tuvalu", "Oceania" },
                    { "TW", "Taiwan, Province of China", "Asia" },
                    { "TZ", "Tanzania, United Republic of", "Africa" },
                    { "UA", "Ukraine", "Europe" },
                    { "UG", "Uganda", "Africa" },
                    { "UM", "United States Minor Outlying Islands", "Oceania" },
                    { "US", "United States of America", "Americas" },
                    { "UY", "Uruguay", "Americas" },
                    { "UZ", "Uzbekistan", "Asia" },
                    { "VA", "Holy See", "Europe" },
                    { "VC", "Saint Vincent and the Grenadines", "Americas" },
                    { "VE", "Venezuela (Bolivarian Republic of)", "Americas" },
                    { "VG", "Virgin Islands (British)", "Americas" },
                    { "VI", "Virgin Islands (U.S.)", "Americas" },
                    { "VN", "Viet Nam", "Asia" },
                    { "VU", "Vanuatu", "Oceania" },
                    { "WF", "Wallis and Futuna", "Oceania" },
                    { "WS", "Samoa", "Oceania" },
                    { "XK", "Kosovo", "Europe" },
                    { "YE", "Yemen", "Asia" },
                    { "YT", "Mayotte", "Africa" },
                    { "ZA", "South Africa", "Africa" },
                    { "ZM", "Zambia", "Africa" },
                    { "ZW", "Zimbabwe", "Africa" }
                });

            migrationBuilder.InsertData(
                table: "Currency",
                columns: new[] { "Code", "MinorUnit", "Name", "Symbol" },
                values: new object[,]
                {
                    { "AED", 2, "UAE Dirham", null },
                    { "AFN", 2, "Afghani", null },
                    { "ALL", 2, "Lek", null },
                    { "AMD", 2, "Armenian Dram", null },
                    { "ANG", 2, "Netherlands Antillean Guilder", null },
                    { "AOA", 2, "Kwanza", null },
                    { "ARS", 2, "Argentine Peso", "$" },
                    { "AUD", 2, "Australian Dollar", "$" },
                    { "AWG", 2, "Aruban Florin", null },
                    { "AZN", 2, "Azerbaijan Manat", null },
                    { "BAM", 2, "Convertible Mark", null },
                    { "BBD", 2, "Barbados Dollar", "$" },
                    { "BDT", 2, "Taka", null },
                    { "BGN", 2, "Bulgarian Lev", null },
                    { "BHD", 3, "Bahraini Dinar", null },
                    { "BIF", 0, "Burundi Franc", null },
                    { "BMD", 2, "Bermudian Dollar", "$" },
                    { "BND", 2, "Brunei Dollar", "$" },
                    { "BOB", 2, "Boliviano", null },
                    { "BOV", 2, "Mvdol", null },
                    { "BRL", 2, "Brazilian Real", "R$" },
                    { "BSD", 2, "Bahamian Dollar", "$" },
                    { "BTN", 2, "Ngultrum", null },
                    { "BWP", 2, "Pula", null },
                    { "BYN", 2, "Belarusian Ruble", null },
                    { "BZD", 2, "Belize Dollar", "$" },
                    { "CAD", 2, "Canadian Dollar", "$" },
                    { "CDF", 2, "Congolese Franc", null },
                    { "CHE", 2, "WIR Euro", null },
                    { "CHF", 2, "Swiss Franc", "Fr" },
                    { "CHW", 2, "WIR Franc", null },
                    { "CLF", 4, "Unidad de Fomento", null },
                    { "CLP", 0, "Chilean Peso", "$" },
                    { "CNY", 2, "Yuan Renminbi", "¥" },
                    { "COP", 2, "Colombian Peso", "$" },
                    { "COU", 2, "Unidad de Valor Real", null },
                    { "CRC", 2, "Costa Rican Colon", null },
                    { "CUC", 2, "Peso Convertible", null },
                    { "CUP", 2, "Cuban Peso", null },
                    { "CVE", 2, "Cabo Verde Escudo", null },
                    { "CZK", 2, "Czech Koruna", null },
                    { "DJF", 0, "Djibouti Franc", null },
                    { "DKK", 2, "Danish Krone", "kr" },
                    { "DOP", 2, "Dominican Peso", "$" },
                    { "DZD", 2, "Algerian Dinar", null },
                    { "EGP", 2, "Egyptian Pound", null },
                    { "ERN", 2, "Nakfa", null },
                    { "ETB", 2, "Ethiopian Birr", null },
                    { "EUR", 2, "Euro", "€" },
                    { "FJD", 2, "Fiji Dollar", "$" },
                    { "FKP", 2, "Falkland Islands Pound", null },
                    { "GBP", 2, "Pound Sterling", "£" },
                    { "GEL", 2, "Lari", null },
                    { "GHS", 2, "Ghana Cedi", null },
                    { "GIP", 2, "Gibraltar Pound", null },
                    { "GMD", 2, "Dalasi", null },
                    { "GNF", 0, "Guinean Franc", null },
                    { "GTQ", 2, "Quetzal", null },
                    { "GYD", 2, "Guyana Dollar", "$" },
                    { "HKD", 2, "Hong Kong Dollar", "$" },
                    { "HNL", 2, "Lempira", null },
                    { "HTG", 2, "Gourde", null },
                    { "HUF", 2, "Forint", null },
                    { "IDR", 2, "Rupiah", null },
                    { "ILS", 2, "New Israeli Sheqel", "₪" },
                    { "INR", 2, "Indian Rupee", "₹" },
                    { "IQD", 3, "Iraqi Dinar", null },
                    { "IRR", 2, "Iranian Rial", null },
                    { "ISK", 0, "Iceland Krona", "kr" },
                    { "JMD", 2, "Jamaican Dollar", "$" },
                    { "JOD", 3, "Jordanian Dinar", null },
                    { "JPY", 0, "Yen", "¥" },
                    { "KES", 2, "Kenyan Shilling", null },
                    { "KGS", 2, "Som", null },
                    { "KHR", 2, "Riel", null },
                    { "KMF", 0, "Comorian Franc", null },
                    { "KPW", 2, "North Korean Won", null },
                    { "KRW", 0, "Won", "₩" },
                    { "KWD", 3, "Kuwaiti Dinar", null },
                    { "KYD", 2, "Cayman Islands Dollar", "$" },
                    { "KZT", 2, "Tenge", null },
                    { "LAK", 2, "Lao Kip", null },
                    { "LBP", 2, "Lebanese Pound", null },
                    { "LKR", 2, "Sri Lanka Rupee", null },
                    { "LRD", 2, "Liberian Dollar", "$" },
                    { "LSL", 2, "Loti", null },
                    { "LYD", 3, "Libyan Dinar", null },
                    { "MAD", 2, "Moroccan Dirham", null },
                    { "MDL", 2, "Moldovan Leu", null },
                    { "MGA", 2, "Malagasy Ariary", null },
                    { "MKD", 2, "Denar", null },
                    { "MMK", 2, "Kyat", null },
                    { "MNT", 2, "Tugrik", null },
                    { "MOP", 2, "Pataca", null },
                    { "MRU", 2, "Ouguiya", null },
                    { "MUR", 2, "Mauritius Rupee", null },
                    { "MVR", 2, "Rufiyaa", null },
                    { "MWK", 2, "Malawi Kwacha", null },
                    { "MXN", 2, "Mexican Peso", "$" },
                    { "MXV", 2, "Mexican Unidad de Inversion (UDI)", null },
                    { "MYR", 2, "Malaysian Ringgit", null },
                    { "MZN", 2, "Mozambique Metical", null },
                    { "NAD", 2, "Namibia Dollar", "$" },
                    { "NGN", 2, "Naira", null },
                    { "NIO", 2, "Cordoba Oro", null },
                    { "NOK", 2, "Norwegian Krone", "kr" },
                    { "NPR", 2, "Nepalese Rupee", null },
                    { "NZD", 2, "New Zealand Dollar", "$" },
                    { "OMR", 3, "Rial Omani", null },
                    { "PAB", 2, "Balboa", null },
                    { "PEN", 2, "Sol", null },
                    { "PGK", 2, "Kina", null },
                    { "PHP", 2, "Philippine Peso", "₱" },
                    { "PKR", 2, "Pakistan Rupee", null },
                    { "PLN", 2, "Zloty", null },
                    { "PYG", 0, "Guarani", null },
                    { "QAR", 2, "Qatari Rial", null },
                    { "RON", 2, "Romanian Leu", null },
                    { "RSD", 2, "Serbian Dinar", null },
                    { "RUB", 2, "Russian Ruble", "₽" },
                    { "RWF", 0, "Rwanda Franc", null },
                    { "SAR", 2, "Saudi Riyal", null },
                    { "SBD", 2, "Solomon Islands Dollar", "$" },
                    { "SCR", 2, "Seychelles Rupee", null },
                    { "SDG", 2, "Sudanese Pound", null },
                    { "SEK", 2, "Swedish Krona", "kr" },
                    { "SGD", 2, "Singapore Dollar", "$" },
                    { "SHP", 2, "Saint Helena Pound", null },
                    { "SLE", 2, "Leone", null },
                    { "SLL", 2, "Leone (pre-2022)", null },
                    { "SOS", 2, "Somali Shilling", null },
                    { "SRD", 2, "Surinam Dollar", "$" },
                    { "SSP", 2, "South Sudanese Pound", null },
                    { "STN", 2, "Dobra", null },
                    { "SVC", 2, "El Salvador Colon", null },
                    { "SYP", 2, "Syrian Pound", null },
                    { "SZL", 2, "Lilangeni", null },
                    { "THB", 2, "Baht", "฿" },
                    { "TJS", 2, "Somoni", null },
                    { "TMT", 2, "Turkmenistan New Manat", null },
                    { "TND", 3, "Tunisian Dinar", null },
                    { "TOP", 2, "Pa’anga", null },
                    { "TRY", 2, "Turkish Lira", "₺" },
                    { "TTD", 2, "Trinidad and Tobago Dollar", "$" },
                    { "TWD", 2, "New Taiwan Dollar", "$" },
                    { "TZS", 2, "Tanzanian Shilling", null },
                    { "UAH", 2, "Hryvnia", "₴" },
                    { "UGX", 0, "Uganda Shilling", null },
                    { "USD", 2, "US Dollar", "$" },
                    { "USN", 2, "US Dollar (Next day)", null },
                    { "UYI", 0, "Uruguay Peso en Unidades Indexadas", null },
                    { "UYU", 2, "Peso Uruguayo", "$" },
                    { "UYW", 4, "Unidad Previsional", null },
                    { "UZS", 2, "Uzbekistan Sum", null },
                    { "VED", 2, "Bolivar Soberano (digital)", null },
                    { "VES", 2, "Bolivar Soberano", null },
                    { "VND", 0, "Dong", "₫" },
                    { "VUV", 0, "Vatu", null },
                    { "WST", 2, "Tala", null },
                    { "XAF", 0, "CFA Franc BEAC", null },
                    { "XAG", 0, "Silver", null },
                    { "XAU", 0, "Gold", null },
                    { "XBA", 0, "European Composite Unit (EURCO)", null },
                    { "XBB", 0, "European Monetary Unit (E.M.U.-6)", null },
                    { "XBC", 0, "European Unit of Account 9 (E.U.A.-9)", null },
                    { "XBD", 0, "European Unit of Account 17 (E.U.A.-17)", null },
                    { "XCD", 2, "East Caribbean Dollar", "$" },
                    { "XCG", 2, "Caribbean Guilder", null },
                    { "XDR", 0, "SDR (Special Drawing Right)", null },
                    { "XOF", 0, "CFA Franc BCEAO", null },
                    { "XPD", 0, "Palladium", null },
                    { "XPF", 0, "CFP Franc", null },
                    { "XPT", 0, "Platinum", null },
                    { "XSU", 0, "Sucre", null },
                    { "XTS", 0, "Codes specifically reserved for testing purposes", null },
                    { "XUA", 0, "ADB Unit of Account", null },
                    { "XXX", 0, "No currency / transactions in no currency", null },
                    { "YER", 2, "Yemeni Rial", null },
                    { "ZAR", 2, "Rand", "R" },
                    { "ZMW", 2, "Zambian Kwacha", null },
                    { "ZWG", 2, "Zimbabwe Gold", null },
                    { "ZWL", 2, "Zimbabwe Dollar (pre-2024)", null }
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
                name: "IX_FundHolding_AssetCategoryCode",
                table: "FundHolding",
                column: "AssetCategoryCode");

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_CountryCode",
                table: "FundHolding",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_CurrencyCode",
                table: "FundHolding",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_FundSnapshotId",
                table: "FundHolding",
                column: "FundSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_FundHolding_IssuerCik",
                table: "FundHolding",
                column: "IssuerCik");

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

            migrationBuilder.CreateIndex(
                name: "IX_Security_CountryCode",
                table: "Security",
                column: "CountryCode");
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
                name: "AssetCategory");

            migrationBuilder.DropTable(
                name: "Currency");

            migrationBuilder.DropTable(
                name: "Security");

            migrationBuilder.DropTable(
                name: "FundSnapshot");

            migrationBuilder.DropTable(
                name: "Country");

            migrationBuilder.DropTable(
                name: "Fund");
        }
    }
}
