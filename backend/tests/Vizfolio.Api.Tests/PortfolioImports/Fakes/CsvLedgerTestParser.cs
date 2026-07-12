using System.Globalization;
using System.Text;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Api.Tests.PortfolioImports.Fakes;

/// <summary>
/// Test-only generic CSV ledger parser. Production no longer ships a generic CSV parser (it carries
/// provider-specific parsers only), but the import service needs a metadata-less, single-account format
/// to exercise its format-agnostic behaviour — a role QFX cannot play because it always carries account
/// metadata. This double stands in for that generic path in unit and endpoint tests.
/// </summary>
public sealed class CsvLedgerTestParser : IPortfolioFileParser
{
    private static readonly string[] RequiredHeaders =
        ["Date", "Type", "Ticker", "Quantity", "Price", "Amount", "Fees", "Currency", "Memo"];

    public string SourceSystem => "CSV";

    public string DisplayName => "Generic CSV ledger (test)";

    // Generic fallback: below any provider-specific parser.
    public int Priority => 0;

    public IReadOnlyCollection<string> FileExtensions { get; } = [".csv"];

    public async Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var line = await ReadFirstNonEmptyLineAsync(reader, cancellationToken);
        if (line is null) return false;

        var header = ParseLine(line)
            .Select(h => h.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RequiredHeaders.All(h => header.Contains(h));
    }

    public async Task<ParsedPortfolioFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var headerLine = await ReadFirstNonEmptyLineAsync(reader, cancellationToken)
            ?? throw new InvalidDataException("CSV file is empty.");
        var headers = ParseLine(headerLine).Select(h => h.Trim()).ToList();
        var idx = BuildIndexMap(headers);

        var transactions = new List<ParsedTransaction>();
        while (await reader.ReadLineAsync(cancellationToken) is { } row)
        {
            if (string.IsNullOrWhiteSpace(row)) continue;
            var fields = ParseLine(row);

            transactions.Add(new ParsedTransaction(
                ExternalId: NullIfBlank(Get(fields, idx, "ExternalId")),
                Type: ParseType(Get(fields, idx, "Type")),
                TradeDate: ParseDate(Get(fields, idx, "Date")),
                SettlementDate: null,
                Ticker: NullIfBlank(Get(fields, idx, "Ticker")),
                Cusip: NullIfBlank(Get(fields, idx, "Cusip")),
                Quantity: ParseNullableDecimal(Get(fields, idx, "Quantity")),
                Price: ParseNullableDecimal(Get(fields, idx, "Price")),
                Amount: ParseNullableDecimal(Get(fields, idx, "Amount")) ?? 0m,
                Fees: ParseNullableDecimal(Get(fields, idx, "Fees")),
                CurrencyCode: NullIfBlank(Get(fields, idx, "Currency")),
                Memo: NullIfBlank(Get(fields, idx, "Memo"))));
        }

        var statement = new ParsedAccountStatement(
            InstitutionCode: null,
            AccountNumber: null,
            Transactions: transactions,
            Positions: [],
            AsOf: null);
        return new ParsedPortfolioFile(SourceSystem, [statement]);
    }

    private static Dictionary<string, int> BuildIndexMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++) map[headers[i]] = i;
        return map;
    }

    private static string? Get(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> idx, string column)
    {
        if (!idx.TryGetValue(column, out var i)) return null;
        return i < fields.Count ? fields[i] : null;
    }

    private static async Task<string?> ReadFirstNonEmptyLineAsync(StreamReader reader, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
        return null;
    }

    private static List<string> ParseLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"' && sb.Length == 0) inQuotes = true;
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static DateOnly ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidDataException("Missing Date column value.");
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        throw new InvalidDataException($"Invalid date '{raw}'.");
    }

    private static TransactionType ParseType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidDataException("Missing Type column value.");
        if (Enum.TryParse<TransactionType>(raw.Trim(), ignoreCase: true, out var t)) return t;
        throw new InvalidDataException($"Unknown transaction type '{raw}'.");
    }

    private static decimal? ParseNullableDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? NullIfBlank(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
