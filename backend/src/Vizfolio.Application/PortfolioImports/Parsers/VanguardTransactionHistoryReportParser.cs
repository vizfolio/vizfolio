using System.Globalization;
using ClosedXML.Excel;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Parsers;

/// <summary>
/// Ingests the per-account "Create a Report" spreadsheet Vanguard offers, which reaches back over the
/// account's full history (unlike the ~18-month QFX export). The report is a single account's ledger:
/// a title block, then a header row (row 4), transaction rows, a blank line, and a DISCLOSURES footer.
/// <para>
/// Columns are matched by header name (order-independent). The verbatim broker type is preserved in
/// <see cref="ParsedTransaction.SourceType"/> while <see cref="ParsedTransaction.Type"/> is normalized.
/// External cash flows (Funds Received / Transfer) take their sign from the label, not the reported
/// amount, whose sign reflects settlement-fund mechanics — see the sign rule below.
/// </para>
/// </summary>
public sealed class VanguardTransactionHistoryReportParser : IPortfolioFileParser
{
    public string SourceSystem => "VANGUARD";

    public string DisplayName => "Vanguard transaction report";

    // Above generic parsers so a Vanguard spreadsheet is claimed here before any catch-all fallback.
    public int Priority => 200;

    // ClosedXML reads the modern OpenXML .xlsx. Legacy binary .xls (BIFF) is advertised for the UI
    // accept hint but will fall through detection until we add a binary reader.
    public IReadOnlyCollection<string> FileExtensions { get; } = [".xlsx", ".xls"];

    /// <summary>
    /// Header labels that together identify a Vanguard transaction report. "Commission &amp; fees" is
    /// the most distinctive; matching ignores the trailing "**". Kept here so tests assert the contract.
    /// </summary>
    internal static readonly string[] SignatureTokens =
        ["Settlement date", "Trade date", "Symbol", "Commission & fees", "Amount"];

    private const int RowsToScanForHeader = 40;

    public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        try
        {
            using var workbook = new XLWorkbook(stream);
            foreach (var worksheet in workbook.Worksheets)
            {
                if (FindHeaderRow(worksheet) is not null) return Task.FromResult(true);
            }
        }
        catch
        {
            // Not a readable .xlsx (or not Vanguard) — let another parser try.
        }

        return Task.FromResult(false);
    }

    public Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);

        foreach (var worksheet in workbook.Worksheets)
        {
            var headerRow = FindHeaderRow(worksheet);
            if (headerRow is null) continue;

            var transactions = ReadTransactions(worksheet, headerRow);
            var statement = new ParsedAccountStatement(
                InstitutionCode: null, AccountNumber: null, Transactions: transactions, Positions: [], AsOf: null);
            return Task.FromResult(new ParsedPortfolioFile(SourceSystem, [statement]));
        }

        throw new InvalidDataException("Vanguard report: could not locate the transaction header row.");
    }

    private static IXLRow? FindHeaderRow(IXLWorksheet worksheet)
    {
        foreach (var row in worksheet.RowsUsed().Take(RowsToScanForHeader))
        {
            var cells = row.CellsUsed().Select(c => Normalize(c.GetString())).ToList();
            if (SignatureTokens.All(token => cells.Any(text => text.Contains(Normalize(token), StringComparison.Ordinal))))
                return row;
        }
        return null;
    }

    private static List<ParsedTransaction> ReadTransactions(IXLWorksheet worksheet, IXLRow headerRow)
    {
        var columns = MapColumns(headerRow);
        var settlementCol = Col(columns, "settlement date");
        var tradeCol = Col(columns, "trade date");
        var symbolCol = Col(columns, "symbol");
        var nameCol = Col(columns, "name");
        var typeCol = Col(columns, "type");
        var quantityCol = Col(columns, "quantity");
        var priceCol = Col(columns, "price");
        var feesCol = Col(columns, "commission & fees");
        var amountCol = Col(columns, "amount");

        var transactions = new List<ParsedTransaction>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();

        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            // Data is contiguous under the header; a blank line then a DISCLOSURES footer ends it.
            if (worksheet.Row(rowNumber).IsEmpty()) break;
            if (CellText(worksheet, rowNumber, 1).StartsWith("DISCLOSURES", StringComparison.OrdinalIgnoreCase))
                break;

            var rawType = CellText(worksheet, rowNumber, typeCol);
            var reportedAmount = CellDecimal(worksheet, rowNumber, amountCol) ?? 0m;
            var (type, amount) = NormalizeType(rawType, reportedAmount);

            // Trade date is the economic date but is occasionally blank (e.g. distributions, transfers);
            // fall back to the always-present settlement date.
            var settlementDate = CellDate(worksheet, rowNumber, settlementCol);
            var tradeDate = CellDate(worksheet, rowNumber, tradeCol) ?? settlementDate
                ?? throw new InvalidDataException($"Row {rowNumber}: no trade or settlement date.");

            var fees = CellDecimal(worksheet, rowNumber, feesCol); // "Free"/blank -> null

            transactions.Add(new ParsedTransaction(
                ExternalId: null, // ID-less format: the import service synthesizes a fingerprint-based id.
                Type: type,
                TradeDate: tradeDate,
                SettlementDate: settlementDate,
                Ticker: NullIfBlank(CellText(worksheet, rowNumber, symbolCol)),
                Cusip: null,
                Quantity: CellDecimal(worksheet, rowNumber, quantityCol),
                Price: CellDecimal(worksheet, rowNumber, priceCol),
                Amount: amount,
                Fees: fees,
                CurrencyCode: null,
                Memo: NullIfBlank(CellText(worksheet, rowNumber, nameCol)),
                SourceType: NullIfBlank(rawType)));
        }

        return transactions;
    }

    /// <summary>
    /// Maps Vanguard's raw type label to a <see cref="TransactionType"/> and fixes the amount sign.
    /// The reported amount reflects settlement-fund mechanics, so for external cash flows we set the sign
    /// from the label: money in is positive, an outgoing "TRANSFER TO" is negative. Everything else keeps
    /// the reported sign (needed for the signed-amount dedup fingerprint). Sweeps map to Other so internal
    /// money-market cash never counts toward contributions.
    /// </summary>
    private static (TransactionType Type, decimal Amount) NormalizeType(string rawType, decimal reportedAmount)
    {
        var t = rawType.Trim().ToLowerInvariant();

        // External cash in. "Contribution" is an IRA contribution — economically a deposit; the raw label
        // is preserved in SourceType so the IRA-specific meaning isn't lost.
        if (t is "funds received" or "contribution")
            return (TransactionType.Deposit, Math.Abs(reportedAmount));
        if (t is "transfer (incoming)")
            return (TransactionType.Transfer, Math.Abs(reportedAmount));
        if (t.StartsWith("transfer to", StringComparison.Ordinal))
            return (TransactionType.Transfer, -Math.Abs(reportedAmount));

        var type = t switch
        {
            "buy" or "buy (exchange)" => TransactionType.Buy,
            "sell" or "sell (exchange)" => TransactionType.Sell,
            "dividend" => TransactionType.Dividend,
            "reinvestment" => TransactionType.Reinvest,
            "interest" => TransactionType.Interest,
            "fee" => TransactionType.Fee,
            _ when t.StartsWith("capital gain", StringComparison.Ordinal) => TransactionType.CapitalGain,
            _ when t.StartsWith("sweep", StringComparison.Ordinal) => TransactionType.Other,
            _ => TransactionType.Other,
        };
        return (type, reportedAmount);
    }

    private static Dictionary<string, int> MapColumns(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = Normalize(cell.GetString());
            if (!string.IsNullOrEmpty(key)) map.TryAdd(key, cell.Address.ColumnNumber);
        }
        return map;
    }

    private static int? Col(IReadOnlyDictionary<string, int> map, string header) =>
        map.TryGetValue(Normalize(header), out var col) ? col : null;

    // Lower-cases, trims, and drops trailing "*" so "Commission & fees**" matches "commission & fees".
    private static string Normalize(string raw) =>
        raw.Trim().TrimEnd('*').Trim().ToLowerInvariant();

    private static string CellText(IXLWorksheet ws, int row, int? col) =>
        col.HasValue ? ws.Cell(row, col.Value).GetString().Trim() : string.Empty;

    private static DateOnly? CellDate(IXLWorksheet ws, int row, int? col)
    {
        if (!col.HasValue) return null;
        var cell = ws.Cell(row, col.Value);
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());

        var s = cell.GetString().Trim();
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static decimal? CellDecimal(IXLWorksheet ws, int row, int? col)
    {
        if (!col.HasValue) return null;
        var cell = ws.Cell(row, col.Value);
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.Number) return cell.GetValue<decimal>();

        var s = cell.GetString().Trim();
        if (string.IsNullOrEmpty(s) || s.Equals("Free", StringComparison.OrdinalIgnoreCase)) return null;

        // Strip currency formatting; accounting-style "(123)" is negative.
        s = s.Replace("$", string.Empty).Replace(",", string.Empty)
             .Replace("(", "-").Replace(")", string.Empty).Trim();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? NullIfBlank(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
