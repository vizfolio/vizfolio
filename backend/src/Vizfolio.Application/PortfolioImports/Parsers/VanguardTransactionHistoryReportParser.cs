using ClosedXML.Excel;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Application.PortfolioImports.Parsers;

/// <summary>
/// Ingests the per-account "Create a Report" spreadsheet Vanguard offers, which reaches back over the
/// account's full history (unlike the ~18-month QFX export).
/// <para>
/// Detection is live and unit-tested today; row parsing is intentionally still a stub — the concrete
/// section-to-<see cref="ParsedTransaction"/> mapping lands once we have a representative sample.
/// </para>
/// </summary>
public sealed class VanguardTransactionHistoryReportParser : IPortfolioFileParser
{
    public string SourceSystem => "VANGUARD";

    public string DisplayName => "Vanguard transaction report";

    // Above generic parsers so a Vanguard spreadsheet is claimed here before any catch-all fallback.
    public int Priority => 200;

    // ClosedXML reads the modern OpenXML .xlsx. Legacy binary .xls (BIFF) is advertised for the UI
    // accept hint but will fall through detection until we add a binary reader — revisit with a sample.
    public IReadOnlyCollection<string> FileExtensions { get; } = [".xlsx", ".xls"];

    /// <summary>
    /// Header labels that together identify a Vanguard transaction report. Kept here (rather than inline)
    /// so tests assert against the same contract. Tighten once a real sample confirms the exact wording.
    /// </summary>
    internal static readonly string[] SignatureTokens =
        ["Account Number", "Trade Date", "Symbol", "Transaction Type"];

    private const int RowsToScan = 40;

    public Task<bool> CanParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        try
        {
            using var workbook = new XLWorkbook(stream);
            foreach (var worksheet in workbook.Worksheets)
            {
                var cellText = worksheet.RowsUsed()
                    .Take(RowsToScan)
                    .SelectMany(row => row.CellsUsed())
                    .Select(cell => cell.GetString())
                    .ToList();

                if (SignatureTokens.All(token =>
                        cellText.Any(text => text.Contains(token, StringComparison.OrdinalIgnoreCase))))
                {
                    return Task.FromResult(true);
                }
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
        // TODO: Map the Vanguard report sections to the parsed model:
        //   - the header block (account number / name) -> ParsedAccountStatement identity
        //   - the transaction-history rows -> ParsedTransaction (Trade Date, Transaction Type, Symbol,
        //     Shares, Share Price, Principal Amount, Commission Fees, Net Amount)
        //   - any holdings/positions section -> ParsedPosition snapshots
        // See docs/performance-api.md before implementing so contribution/snapshot semantics stay aligned.
        throw new NotSupportedException("Vanguard report parsing not yet implemented.");
    }
}
