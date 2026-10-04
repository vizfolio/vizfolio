using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Parsers;

/// <summary>
/// Ingests M1 Finance's activity export — a CSV, since M1 offers no OFX/QFX. One file is one account's ledger with
/// the columns <c>Date, Posted Date, Symbol, Description, Transaction Type, Amount, Units, Unit Type, Unit Price,
/// Security Id, Security Id Type</c>, matched by header name (order-independent).
/// <para>
/// Checked against a real export: dates print as <c>"Nov 17, 2025"</c> (quoted, so the comma survives), <c>--</c>
/// marks an empty cell, trailing empty columns may be dropped, and <b>amounts are unsigned</b> — the direction comes
/// from the type (and, for transfers, the description), so this parser signs them the way the cash moves. Labels seen:
/// <c>TRANSFER</c> ("ACH deposit of $… completed."), <c>PURCHASED</c>, <c>DIVIDEND</c> (cash, units <c>--</c>) and
/// <c>CASH</c> (<c>PROMO_CREDIT_…</c>, a sign-up bonus, taken as income). <c>SOLD</c>, <c>INTEREST</c>, <c>FEE</c>
/// and withdrawals are inferred counterparts; anything else is stored as <c>Other</c> with an import warning.
/// </para>
/// <para>
/// The file names no account, only the broker: the import routes it by the transactions it shares with an existing
/// account, or asks. M1 holds plain cash (no settlement fund) and reports no positions.
/// </para>
/// </summary>
public sealed class M1FinanceActivityCsvParser : IPortfolioFileParser
{
    /// <summary>The institution code a new account created for an M1 file starts out at (M1 has no OFX <c>BROKERID</c>).</summary>
    public const string InstitutionCode = "m1.com";

    public string SourceSystem => "M1";

    public string DisplayName => "M1 Finance activity (CSV)";

    // Above generic parsers so an M1 export is claimed here before any catch-all CSV fallback.
    public int Priority => 200;

    public IReadOnlyCollection<string> FileExtensions { get; } = [".csv"];

    /// <summary>Header labels that together identify an M1 activity export. Kept here so tests assert the contract.</summary>
    internal static readonly string[] SignatureTokens =
        ["Date", "Posted Date", "Transaction Type", "Unit Type", "Security Id Type"];

    public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var headers = CsvTable.ReadHeaders(stream).Select(h => h.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(SignatureTokens.All(headers.Contains));
        }
        catch
        {
            // Not readable as text (or not M1) — let another parser try.
            return Task.FromResult(false);
        }
    }

    public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var table = CsvTable.Read(stream);
        if (!table.HasColumns(SignatureTokens))
            throw new InvalidDataException("M1 activity export: could not find the expected header row.");

        var warnings = new ImportWarningCollector();
        var columns = new Columns(table);
        var transactions = new List<ParsedTransaction>();
        foreach (var row in table.Rows)
        {
            if (ReadRow(row, columns, warnings) is { } transaction)
                transactions.Add(transaction);
        }

        var statement = new ParsedAccountStatement(
            InstitutionCode: InstitutionCode, AccountNumber: null,
            Transactions: transactions, Positions: [], AsOf: null);
        return Task.FromResult(new ParsedPortfolioFile(SourceSystem, [statement]) { Warnings = warnings.ToList() });
    }

    private sealed class Columns(CsvTable table)
    {
        public int? Date { get; } = table.Column("Date");
        public int? PostedDate { get; } = table.Column("Posted Date");
        public int? Symbol { get; } = table.Column("Symbol");
        public int? Description { get; } = table.Column("Description");
        public int? Type { get; } = table.Column("Transaction Type");
        public int? Amount { get; } = table.Column("Amount");
        public int? Units { get; } = table.Column("Units");
        public int? UnitType { get; } = table.Column("Unit Type");
        public int? UnitPrice { get; } = table.Column("Unit Price");
        public int? SecurityId { get; } = table.Column("Security Id");
        public int? SecurityIdType { get; } = table.Column("Security Id Type");
    }

    private static ParsedTransaction? ReadRow(CsvTable.Row row, Columns columns, ImportWarningCollector warnings)
    {
        string? Text(int? column) => ReportValues.NullIfBlank(CsvTable.Field(row, column));
        var sample = $"line {row.LineNumber}";

        // "Date" is when M1 dates the activity; "Posted Date" (sometimes a day earlier, e.g. a dividend's pay date
        // falling before a weekend) is kept as the settlement date.
        var postedDate = ReportValues.ParseDate(Text(columns.PostedDate));
        if ((ReportValues.ParseDate(Text(columns.Date)) ?? postedDate) is not { } tradeDate)
        {
            warnings.Add(ImportWarningCodes.RowFailed, "A row with no readable date was skipped.", sample);
            return null;
        }

        var rawType = Text(columns.Type) ?? string.Empty;
        var description = Text(columns.Description);
        var ticker = Text(columns.Symbol)?.ToUpperInvariant();
        var isShares = string.Equals(Text(columns.UnitType), "SHARES", StringComparison.OrdinalIgnoreCase);
        var units = isShares ? ReportValues.ParseMoney(Text(columns.Units)) : null;
        var reportedAmount = ReportValues.ParseMoney(Text(columns.Amount)) ?? 0m;

        var (type, amount, quantity) = Normalize(rawType, description, isShares, reportedAmount, units);
        if (type is null)
        {
            warnings.Add(ImportWarningCodes.UnmappedLabel,
                $"\"{rawType}\"{(isShares ? " (shares)" : string.Empty)} isn't an M1 activity Vizfolio knows; those rows were imported as Other (their cash is counted at the reported sign, any shares are applied).",
                description is null ? sample : $"{sample}: {description}");
        }

        return new ParsedTransaction(
            ExternalId: null, // ID-less format: the import service synthesizes a fingerprint-based id.
            Type: type ?? TransactionType.Other,
            TradeDate: tradeDate,
            SettlementDate: postedDate,
            Ticker: ticker,
            Cusip: ReadCusip(Text(columns.SecurityId), Text(columns.SecurityIdType), ticker, warnings, sample),
            Quantity: quantity,
            Price: ReportValues.ParseMoney(Text(columns.UnitPrice)),
            Amount: amount,
            Fees: null,
            CurrencyCode: null,
            Memo: description,
            SourceType: ReportValues.NullIfBlank(rawType));
    }

    /// <summary>
    /// Maps M1's label to a <see cref="TransactionType"/> and signs the (unsigned) amount by the way cash moves:
    /// money in is positive, money out negative. A null type is a label this parser doesn't know (stored as Other at
    /// the reported sign). An amount the file does sign negative is respected where the direction can vary.
    /// </summary>
    private static (TransactionType? Type, decimal Amount, decimal? Quantity) Normalize(
        string rawType, string? description, bool isShares, decimal amount, decimal? units)
    {
        var magnitude = Math.Abs(amount);
        switch (rawType.Trim().ToUpperInvariant())
        {
            case "PURCHASED":
                return (TransactionType.Buy, -magnitude, Abs(units));
            case "SOLD":
                return (TransactionType.Sell, magnitude, -Abs(units));
            case "DIVIDEND":
                return (TransactionType.Dividend, amount, units);
            case "INTEREST":
                return (TransactionType.Interest, amount, units);
            case "FEE":
                return (TransactionType.Fee, -magnitude, null);
            case "TRANSFER" when !isShares:
                var isWithdrawal = amount < 0m || Mentions(description, "withdraw");
                return isWithdrawal
                    ? (TransactionType.Withdrawal, -magnitude, null)
                    : (TransactionType.Deposit, magnitude, null);
            // A sign-up/funding bonus is money the user didn't put in, so it counts toward their return (and is
            // taxed as income): stored as Interest, with M1's label kept in SourceType.
            case "CASH" when description?.StartsWith("PROMO_CREDIT", StringComparison.OrdinalIgnoreCase) == true:
                return (TransactionType.Interest, amount, null);
            default:
                return (null, amount, units);
        }
    }

    private static string? ReadCusip(string? securityId, string? idType, string? ticker, ImportWarningCollector warnings, string sample)
    {
        if (securityId is null) return null;
        if (string.Equals(idType, "CUSIP", StringComparison.OrdinalIgnoreCase)) return securityId;

        if (ticker is null)
            warnings.Add(ImportWarningCodes.UnsupportedSecurityId,
                $"A security identified only by a {idType ?? "unlabelled"} ID can't be looked up; the row was imported without a security.",
                sample);
        return null;
    }

    private static bool Mentions(string? text, string word)
        => text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    private static decimal? Abs(decimal? value) => value is { } v ? Math.Abs(v) : null;
}
