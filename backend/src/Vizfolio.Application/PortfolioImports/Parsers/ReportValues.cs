using System.Globalization;

namespace Vizfolio.Application.PortfolioImports.Parsers;

/// <summary>
/// Reads the text values broker reports (spreadsheets, CSVs) print: money with currency formatting, dates in the
/// broker's display format, and placeholders like <c>--</c> for "nothing here".
/// </summary>
internal static class ReportValues
{
    // "MMM d, yyyy" is M1's "Nov 17, 2025"; "M/d/yyyy" is the common US export format.
    private static readonly string[] DateFormats = ["MMM d, yyyy", "MMMM d, yyyy", "M/d/yyyy", "yyyy-MM-dd"];

    /// <summary>
    /// A money or number cell: strips <c>$</c> and thousands separators, reads accounting-style <c>(123)</c> as
    /// negative. Null for blank, <c>--</c>, <c>Free</c> (Vanguard's zero commission) or anything unreadable.
    /// </summary>
    public static decimal? ParseMoney(string? raw)
    {
        var s = NullIfBlank(raw);
        if (s is null || s.Equals("Free", StringComparison.OrdinalIgnoreCase)) return null;

        s = s.Replace("$", string.Empty).Replace(",", string.Empty)
             .Replace("(", "-").Replace(")", string.Empty).Trim();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>A date cell in one of the formats brokers print; null when blank or unreadable.</summary>
    public static DateOnly? ParseDate(string? raw)
    {
        var s = NullIfBlank(raw);
        if (s is null) return null;
        if (DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            return exact;
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>The trimmed text, or null when blank or a <c>--</c> placeholder.</summary>
    public static string? NullIfBlank(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        return s == "--" ? null : s;
    }
}
