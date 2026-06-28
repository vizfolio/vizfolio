using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

public sealed class PortfolioImportService : IPortfolioImportService
{
    private readonly IAppDbContext _db;
    private readonly IEnumerable<IPortfolioFileParser> _parsers;
    private readonly ILogger<PortfolioImportService> _logger;

    public PortfolioImportService(
        IAppDbContext db,
        IEnumerable<IPortfolioFileParser> parsers,
        ILogger<PortfolioImportService> logger)
    {
        _db = db;
        _parsers = parsers;
        _logger = logger;
    }

    public async Task<PortfolioImportResult> ImportAsync(
        Guid accountId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var accountExists = await _db.Accounts.AsNoTracking()
            .AnyAsync(a => a.AccountId == accountId, cancellationToken);
        if (!accountExists)
            return PortfolioImportResult.AccountNotFound(stopwatch.Elapsed);

        await using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);

        IPortfolioFileParser? chosen = null;
        foreach (var parser in _parsers)
        {
            buffer.Position = 0;
            if (await parser.CanParseAsync(buffer, fileName, cancellationToken))
            {
                chosen = parser;
                break;
            }
        }

        if (chosen is null)
        {
            _logger.LogWarning("No parser matched uploaded file {FileName} for account {AccountId}", fileName, accountId);
            return PortfolioImportResult.UnsupportedFormat(stopwatch.Elapsed);
        }

        buffer.Position = 0;
        var parsed = await chosen.ParseAsync(buffer, fileName, cancellationToken);

        var existingKeys = await _db.AccountTransactions
            .Where(t => t.AccountId == accountId && t.SourceSystem == parsed.SourceSystem)
            .Select(t => t.ExternalId)
            .ToListAsync(cancellationToken);
        var existing = existingKeys.ToHashSet(StringComparer.Ordinal);

        var tickerSet = parsed.Transactions
            .Select(t => t.Ticker)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var resolver = new AccountHoldingResolver(_db, _logger);
        await resolver.PrimeAsync(accountId, tickerSet, cancellationToken);

        var validCurrencies = await _db.Currencies.AsNoTracking()
            .Select(c => c.Code).ToListAsync(cancellationToken);
        var currencySet = validCurrencies.ToHashSet(StringComparer.Ordinal);

        var failures = new List<PortfolioImportFailure>();
        var inserted = 0;
        var skipped = 0;

        foreach (var (parsedTx, index) in parsed.Transactions.Select((t, i) => (t, i)))
        {
            try
            {
                var externalId = !string.IsNullOrWhiteSpace(parsedTx.ExternalId)
                    ? parsedTx.ExternalId!.Trim()
                    : ComputeHash(accountId, parsedTx);

                if (existing.Contains(externalId))
                {
                    skipped++;
                    continue;
                }

                var entity = new AccountTransaction(
                    accountId: accountId,
                    sourceSystem: parsed.SourceSystem,
                    externalId: externalId,
                    type: parsedTx.Type,
                    tradeDate: parsedTx.TradeDate,
                    amount: parsedTx.Amount);

                entity.SetSecurityReference(parsedTx.Ticker, parsedTx.Cusip);
                entity.SetTradeDetails(parsedTx.Quantity, parsedTx.Price, parsedTx.Fees, parsedTx.SettlementDate);

                var currency = NormalizeCurrency(parsedTx.CurrencyCode, currencySet);
                entity.SetCurrency(currency);

                entity.SetMemo(parsedTx.Memo);

                if (!string.IsNullOrWhiteSpace(entity.Ticker))
                {
                    var holding = resolver.ResolveByTicker(entity.Ticker!, entity.Cusip, entity.CurrencyCode);
                    if (holding is not null)
                        entity.LinkToHolding(holding.AccountHoldingId);
                }

                _db.AccountTransactions.Add(entity);
                existing.Add(externalId);
                inserted++;
            }
            catch (Exception ex)
            {
                failures.Add(new PortfolioImportFailure(
                    Key: $"row[{index}]",
                    Reason: ex.Message));
            }
        }

        if (inserted > 0)
            await _db.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "Imported {Inserted} transactions ({Skipped} skipped, {Failed} failed) for account {AccountId} from {Source}",
            inserted, skipped, failures.Count, accountId, parsed.SourceSystem);

        return new PortfolioImportResult(
            Status: PortfolioImportStatus.Success,
            SourceSystem: parsed.SourceSystem,
            SourceInstitution: parsed.SourceInstitution,
            SourceAccountNumber: parsed.SourceAccountNumber,
            Considered: parsed.Transactions.Count,
            Inserted: inserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures,
            Duration: stopwatch.Elapsed);
    }

    private static string? NormalizeCurrency(string? raw, IReadOnlySet<string> validCodes)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToUpperInvariant();
        return validCodes.Contains(normalized) ? normalized : null;
    }

    private static string ComputeHash(Guid accountId, ParsedTransaction tx)
    {
        var raw = string.Join('|',
            accountId.ToString("N"),
            tx.TradeDate.ToString("yyyy-MM-dd"),
            tx.Type.ToString(),
            (tx.Ticker ?? string.Empty).ToUpperInvariant(),
            tx.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            tx.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var sb = new StringBuilder(32);
        for (var i = 0; i < 16; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
