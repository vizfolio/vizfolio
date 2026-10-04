using System.Text;

namespace Vizfolio.Application.PortfolioImports.Parsers;

/// <summary>
/// A CSV file read as a header row plus data rows (RFC 4180: quoted fields may hold commas, line breaks and
/// <c>""</c>-escaped quotes; CRLF or LF; a UTF-8/16 byte-order mark is skipped). Rows shorter than the header are
/// padded with empty fields — brokers drop trailing empty columns — and blank lines are skipped. Columns are looked
/// up by header name (trimmed, case-insensitive), so a broker reordering its columns doesn't break a parser.
/// </summary>
internal sealed class CsvTable
{
    /// <summary>One data row, with the file line it starts on (1-based) for warning samples.</summary>
    public sealed record Row(int LineNumber, IReadOnlyList<string> Fields);

    private readonly Dictionary<string, int> _columns;

    private CsvTable(IReadOnlyList<string> headers, IReadOnlyList<Row> rows)
    {
        Headers = headers;
        Rows = rows;
        _columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = NormalizeHeader(headers[i]);
            if (key.Length > 0) _columns.TryAdd(key, i);
        }
    }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<Row> Rows { get; }

    /// <summary>The column index of <paramref name="header"/>, or null when the file has no such column.</summary>
    public int? Column(string header) => _columns.TryGetValue(NormalizeHeader(header), out var i) ? i : null;

    public bool HasColumns(IEnumerable<string> headers) => headers.All(h => Column(h) is not null);

    /// <summary>The field in <paramref name="column"/> of <paramref name="row"/>, or empty when the column is absent.</summary>
    public static string Field(Row row, int? column)
        => column is { } c && c < row.Fields.Count ? row.Fields[c] : string.Empty;

    // Detection reads at most this much of the file: a header row is short, and a binary file has no line breaks.
    private const int HeaderScanChars = 8192;

    /// <summary>Reads the whole stream from its start. A file with no non-blank line has no headers and no rows.</summary>
    public static CsvTable Read(Stream stream)
    {
        using var reader = OpenReader(stream);
        return Read(reader, maxRows: null);
    }

    /// <summary>
    /// Reads just the header row from the first few kilobytes — enough for format detection without reading a large
    /// (or binary) file.
    /// </summary>
    public static IReadOnlyList<string> ReadHeaders(Stream stream)
    {
        using var reader = OpenReader(stream);
        var buffer = new char[HeaderScanChars];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return Read(new StringReader(new string(buffer, 0, read)), maxRows: 0).Headers;
    }

    private static StreamReader OpenReader(Stream stream)
    {
        stream.Position = 0;
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    }

    private static CsvTable Read(TextReader reader, int? maxRows)
    {
        IReadOnlyList<string>? headers = null;
        var rows = new List<Row>();
        var line = 1;
        while (ReadRecord(reader, ref line) is { } record)
        {
            if (record.Fields.All(string.IsNullOrWhiteSpace)) continue;
            if (headers is null)
            {
                headers = record.Fields.Select(f => f.Trim()).ToList();
                if (maxRows == 0) break;
                continue;
            }

            var fields = record.Fields.ToList();
            while (fields.Count < headers.Count) fields.Add(string.Empty);
            rows.Add(new Row(record.LineNumber, fields));
            if (rows.Count == maxRows) break;
        }

        return new CsvTable(headers ?? [], rows);
    }

    /// <summary>The next record (which may span lines inside quotes), or null at the end of the stream.</summary>
    private static Row? ReadRecord(TextReader reader, ref int line)
    {
        if (reader.Peek() < 0) return null;

        var startLine = line;
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        while (true)
        {
            var c = reader.Read();
            if (c < 0)
            {
                fields.Add(field.ToString());
                return new Row(startLine, fields);
            }

            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else inQuotes = false;
                }
                else
                {
                    if (ch == '\n') line++;
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (reader.Peek() == '\n') reader.Read();
                    line++;
                    fields.Add(field.ToString());
                    return new Row(startLine, fields);
                case '\n':
                    line++;
                    fields.Add(field.ToString());
                    return new Row(startLine, fields);
                default:
                    field.Append(ch);
                    break;
            }
        }
    }

    private static string NormalizeHeader(string raw) => raw.Trim().ToLowerInvariant();
}
