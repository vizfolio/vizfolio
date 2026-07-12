using System.Diagnostics;
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

    public async Task<PortfolioImportResult> ImportToAccountAsync(
        Guid accountId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken,
        string? requestedSourceSystem = null)
    {
        var stopwatch = Stopwatch.StartNew();

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
        if (account is null)
            return PortfolioImportResult.AccountNotFound(stopwatch.Elapsed);

        var (parsed, missing) = await ParseAsync(fileStream, fileName, requestedSourceSystem, cancellationToken);
        if (missing is not null)
            return missing(stopwatch.Elapsed);

        if (parsed!.Statements.Any(s => s.InstitutionCode is not null || s.AccountNumber is not null))
            return PortfolioImportResult.FileHasAccountInfo(parsed.SourceSystem, stopwatch.Elapsed);

        var allTransactions = parsed.Statements.SelectMany(s => s.Transactions).ToList();
        var allPositions = parsed.Statements.SelectMany(s => s.Positions).ToList();
        var mergedAsOf = parsed.Statements
            .Where(s => s.AsOf is not null)
            .Select(s => s.AsOf!.Value)
            .OrderByDescending(d => d)
            .Cast<DateOnly?>()
            .FirstOrDefault();
        var mergedStatement = new ParsedAccountStatement(
            InstitutionCode: null,
            AccountNumber: null,
            Transactions: allTransactions,
            Positions: allPositions,
            AsOf: mergedAsOf);

        var currencySet = await LoadCurrenciesAsync(cancellationToken);
        var accountResult = await ImportStatementAsync(
            account, parsed.SourceSystem, mergedStatement, accountCreated: false, currencySet, cancellationToken);

        if (HasPendingChanges())
            await _db.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        return new PortfolioImportResult(
            Status: PortfolioImportStatus.Success,
            SourceSystem: parsed.SourceSystem,
            Accounts: [accountResult],
            Duration: stopwatch.Elapsed);
    }

    public async Task<PortfolioImportResult> ImportToPortfolioAsync(
        Guid portfolioId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken,
        string? requestedSourceSystem = null)
    {
        var stopwatch = Stopwatch.StartNew();

        var portfolioExists = await _db.Portfolios.AsNoTracking()
            .AnyAsync(p => p.PortfolioId == portfolioId, cancellationToken);
        if (!portfolioExists)
            return PortfolioImportResult.PortfolioNotFound(stopwatch.Elapsed);

        var (parsed, missing) = await ParseAsync(fileStream, fileName, requestedSourceSystem, cancellationToken);
        if (missing is not null)
            return missing(stopwatch.Elapsed);

        var identifiable = parsed!.Statements
            .Where(s => !string.IsNullOrWhiteSpace(s.InstitutionCode) && !string.IsNullOrWhiteSpace(s.AccountNumber))
            .ToList();

        if (identifiable.Count == 0)
            return PortfolioImportResult.FileHasNoAccountInfo(parsed.SourceSystem, stopwatch.Elapsed);

        var existingAccounts = await _db.Accounts
            .Where(a => a.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);
        var accountByKey = existingAccounts.ToDictionary(
            a => AccountKey(a.InstitutionCode, a.AccountNumber),
            a => a);

        var currencySet = await LoadCurrenciesAsync(cancellationToken);
        var perAccountResults = new List<AccountImportResult>(identifiable.Count);

        foreach (var statement in identifiable)
        {
            var key = AccountKey(statement.InstitutionCode!, statement.AccountNumber!);
            var created = false;
            if (!accountByKey.TryGetValue(key, out var account))
            {
                account = Account.FromImport(portfolioId, statement.InstitutionCode!, statement.AccountNumber!);
                _db.Accounts.Add(account);
                accountByKey[key] = account;
                created = true;
            }

            var result = await ImportStatementAsync(
                account, parsed.SourceSystem, statement, accountCreated: created, currencySet, cancellationToken);
            perAccountResults.Add(result);
        }

        if (HasPendingChanges())
            await _db.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "Imported {Inserted} transactions across {AccountCount} accounts in portfolio {PortfolioId} from {Source}",
            perAccountResults.Sum(r => r.Inserted), perAccountResults.Count, portfolioId, parsed.SourceSystem);

        return new PortfolioImportResult(
            Status: PortfolioImportStatus.Success,
            SourceSystem: parsed.SourceSystem,
            Accounts: perAccountResults,
            Duration: stopwatch.Elapsed);
    }

    private async Task<AccountImportResult> ImportStatementAsync(
        Account account,
        string sourceSystem,
        ParsedAccountStatement statement,
        bool accountCreated,
        IReadOnlySet<string> validCurrencies,
        CancellationToken cancellationToken)
    {
        // Load existing rows across ALL sources so a trade present in both (e.g.) a QFX export and a
        // Vanguard report is caught, not just same-file re-uploads.
        var existingRows = accountCreated
            ? []
            : await _db.AccountTransactions
                .Where(t => t.AccountId == account.AccountId)
                .Select(t => new { t.SourceSystem, t.ExternalId, t.TradeDate, t.Ticker, t.Quantity, t.Amount })
                .ToListAsync(cancellationToken);

        // Exact (source, externalId) fast-path — keeps same-file re-imports idempotent.
        var existingExternalIds = existingRows
            .Select(r => ExternalKey(r.SourceSystem, r.ExternalId))
            .ToHashSet(StringComparer.Ordinal);

        // Cross-source fingerprint multiset — skip an incoming row only while an unmatched existing row
        // with the same economic fingerprint remains, so genuine same-day duplicates are preserved.
        var fingerprintCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in existingRows)
        {
            var fp = TransactionFingerprint.Compute(account.AccountId, r.TradeDate, r.Ticker, r.Quantity, r.Amount);
            fingerprintCounts[fp] = fingerprintCounts.GetValueOrDefault(fp) + 1;
        }

        var tickerSet = statement.Transactions
            .Select(t => t.Ticker)
            .Concat(statement.Positions.Select(p => p.Ticker))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var resolver = new AccountHoldingResolver(_db, _logger);
        await resolver.PrimeAsync(account.AccountId, tickerSet, cancellationToken);

        var failures = new List<PortfolioImportFailure>();
        var inserted = 0;
        var skipped = 0;

        foreach (var (parsedTx, index) in statement.Transactions.Select((t, i) => (t, i)))
        {
            try
            {
                var fingerprint = TransactionFingerprint.Compute(account.AccountId, parsedTx);

                // ID-less formats (e.g. the Vanguard report) get a stable, unique synthetic id so genuine
                // same-day duplicates remain storable and a same-file re-upload stays idempotent.
                var externalId = !string.IsNullOrWhiteSpace(parsedTx.ExternalId)
                    ? parsedTx.ExternalId!.Trim()
                    : $"{fingerprint}-{index}";

                // Same-source exact duplicate (re-upload of the same file).
                if (existingExternalIds.Contains(ExternalKey(sourceSystem, externalId)))
                {
                    skipped++;
                    continue;
                }

                // Cross-source / overlap duplicate: an existing row already covers this economic event.
                if (fingerprintCounts.TryGetValue(fingerprint, out var remaining) && remaining > 0)
                {
                    fingerprintCounts[fingerprint] = remaining - 1;
                    skipped++;
                    continue;
                }

                var entity = new AccountTransaction(
                    accountId: account.AccountId,
                    sourceSystem: sourceSystem,
                    externalId: externalId,
                    type: parsedTx.Type,
                    tradeDate: parsedTx.TradeDate,
                    amount: parsedTx.Amount);

                entity.SetSecurityReference(parsedTx.Ticker, parsedTx.Cusip);
                entity.SetTradeDetails(parsedTx.Quantity, parsedTx.Price, parsedTx.Fees, parsedTx.SettlementDate);
                entity.SetCurrency(NormalizeCurrency(parsedTx.CurrencyCode, validCurrencies));
                entity.SetMemo(parsedTx.Memo);
                entity.SetSourceType(parsedTx.SourceType);

                if (!string.IsNullOrWhiteSpace(entity.Ticker))
                {
                    var holding = resolver.ResolveByTicker(entity.Ticker!, entity.Cusip, entity.CurrencyCode);
                    if (holding is not null)
                        entity.LinkToHolding(holding.AccountHoldingId);
                }

                _db.AccountTransactions.Add(entity);
                existingExternalIds.Add(ExternalKey(sourceSystem, externalId));
                inserted++;
            }
            catch (Exception ex)
            {
                failures.Add(new PortfolioImportFailure(
                    Key: $"row[{index}]",
                    Reason: ex.Message));
            }
        }

        await ImportPositionSnapshotsAsync(statement, resolver, validCurrencies, cancellationToken);

        return new AccountImportResult(
            AccountId: account.AccountId,
            Created: accountCreated,
            InstitutionCode: account.InstitutionCode,
            AccountNumber: account.AccountNumber,
            Considered: statement.Transactions.Count,
            Inserted: inserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures);
    }

    private async Task ImportPositionSnapshotsAsync(
        ParsedAccountStatement statement,
        AccountHoldingResolver resolver,
        IReadOnlySet<string> validCurrencies,
        CancellationToken cancellationToken)
    {
        if (statement.Positions.Count == 0) return;

        var inFlight = new HashSet<(Guid HoldingId, DateOnly AsOf)>();

        foreach (var pos in statement.Positions)
        {
            if (string.IsNullOrWhiteSpace(pos.Ticker)) continue;

            var ticker = pos.Ticker.Trim().ToUpperInvariant();
            var holding = resolver.ResolveByTicker(ticker, pos.Cusip, pos.CurrencyCode);
            if (holding is null)
            {
                _logger.LogDebug(
                    "Skipping position snapshot for unresolved ticker {Ticker} on {AsOf}", ticker, pos.AsOf);
                continue;
            }

            var key = (holding.AccountHoldingId, pos.AsOf);
            if (!inFlight.Add(key)) continue;

            var alreadyExists = await _db.AccountHoldingSnapshots
                .AnyAsync(s => s.AccountHoldingId == holding.AccountHoldingId && s.AsOf == pos.AsOf,
                    cancellationToken);
            if (alreadyExists) continue;

            var snapshot = new AccountHoldingSnapshot(
                holding.AccountHoldingId, pos.AsOf, pos.Units, AccountHoldingSnapshotSource.BrokerPosition);
            snapshot.SetValuation(
                pos.CostBasis,
                pos.MarketValue,
                pos.UnitPrice,
                NormalizeCurrency(pos.CurrencyCode, validCurrencies));
            _db.AccountHoldingSnapshots.Add(snapshot);
        }
    }

    private async Task<(ParsedPortfolioFile? Parsed, Func<TimeSpan, PortfolioImportResult>? Missing)> ParseAsync(
        Stream fileStream, string fileName, string? requestedSourceSystem, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);

        // Explicit override from the UI: trust the caller's pick and skip auto-detection.
        if (!string.IsNullOrWhiteSpace(requestedSourceSystem))
        {
            var requested = requestedSourceSystem.Trim();
            var forced = _parsers.FirstOrDefault(
                p => string.Equals(p.SourceSystem, requested, StringComparison.OrdinalIgnoreCase));
            if (forced is null)
            {
                _logger.LogWarning("No parser registered for requested source system {SourceSystem}", requested);
                return (null, d => PortfolioImportResult.UnknownParser(requested, d));
            }

            buffer.Position = 0;
            var forcedParse = await forced.ParseAsync(buffer, fileName, cancellationToken);
            return (forcedParse, null);
        }

        // Auto-detect: offer the file to parsers from highest priority to lowest so a
        // provider-specific parser gets first refusal ahead of any generic fallback.
        IPortfolioFileParser? chosen = null;
        foreach (var parser in _parsers.OrderByDescending(p => p.Priority))
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
            _logger.LogWarning("No parser matched uploaded file {FileName}", fileName);
            return (null, PortfolioImportResult.UnsupportedFormat);
        }

        buffer.Position = 0;
        var parsed = await chosen.ParseAsync(buffer, fileName, cancellationToken);
        return (parsed, null);
    }

    private async Task<HashSet<string>> LoadCurrenciesAsync(CancellationToken cancellationToken)
    {
        var codes = await _db.Currencies.AsNoTracking()
            .Select(c => c.Code).ToListAsync(cancellationToken);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    private bool HasPendingChanges()
    {
        if (_db is DbContext ctx) return ctx.ChangeTracker.HasChanges();
        return true;
    }

    private static string AccountKey(string institutionCode, string accountNumber) =>
        $"{institutionCode.ToLowerInvariant()}|{accountNumber}";

    private static string? NormalizeCurrency(string? raw, IReadOnlySet<string> validCodes)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToUpperInvariant();
        return validCodes.Contains(normalized) ? normalized : null;
    }

    private static string ExternalKey(string sourceSystem, string externalId) =>
        $"{sourceSystem.Trim().ToUpperInvariant()}|{externalId}";
}
