using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.Portfolios;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>
/// Imports broker files into a portfolio's ledger. Every upload is recorded as an <see cref="ImportBatch"/> holding the
/// file itself, so it can be re-parsed when a parser improves (<see cref="ReprocessAsync"/>) and undone
/// (<see cref="IImportUndoService"/>). Re-uploading the same file is recognised by its hash and writes nothing.
/// Rows are deduplicated by <c>(source, externalId)</c> and, across sources, by economic fingerprint; a row already
/// stored from the same source is brought in line with what the parser now says (roadmap Appendix A.11).
/// See docs/performance-api.md → "Importing files".
/// </summary>
public sealed class PortfolioImportService : IPortfolioImportService
{
    private static readonly JsonSerializerOptions SummaryJson = new(JsonSerializerDefaults.Web);

    private readonly IAppDbContext _db;
    private readonly IEnumerable<IPortfolioFileParser> _parsers;
    private readonly IImpliedContributionService _impliedContributions;
    private readonly ILedgerRelinker? _ledgerRelinker;
    private readonly IPriceRefreshQueue? _priceRefresh;
    private readonly ILogger<PortfolioImportService> _logger;

    public PortfolioImportService(
        IAppDbContext db,
        IEnumerable<IPortfolioFileParser> parsers,
        IImpliedContributionService impliedContributions,
        ILogger<PortfolioImportService> logger,
        ILedgerRelinker? ledgerRelinker = null,
        IPriceRefreshQueue? priceRefresh = null)
    {
        _priceRefresh = priceRefresh;
        _db = db;
        _parsers = parsers;
        _impliedContributions = impliedContributions;
        _ledgerRelinker = ledgerRelinker;
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

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
        if (account is null)
            return PortfolioImportResult.AccountNotFound(stopwatch.Elapsed);

        var file = await ImportFile.ReadAsync(fileStream, fileName, cancellationToken);
        if (await FindActiveBatchAsync(account.PortfolioId, accountId, file.Sha256, cancellationToken) is { } previous)
            return AlreadyImported(previous, stopwatch.Elapsed);

        var parse = await ParseAsync(file, requestedSourceSystem, cancellationToken);
        if (parse.Failure is not null)
            return parse.Failure(stopwatch.Elapsed);
        var parsed = parse.File!;

        var warnings = new ImportWarningCollector();
        warnings.AddRange(parsed.Warnings);
        var statements = RouteToAccount(account, parsed, warnings);
        if (statements is null)
            return new PortfolioImportResult(PortfolioImportStatus.AccountMismatch, parsed.SourceSystem, [], stopwatch.Elapsed)
            {
                FileAccountNumbers = parsed.Statements
                    .Where(HasAccountNumber).Select(s => MaskAccountNumber(s.AccountNumber!)).Distinct().ToList(),
            };

        var batch = file.ToBatch(account.PortfolioId, accountId, parsed.SourceSystem);
        return await RunAsync(batch, parsed.SourceSystem, [new ImportTarget(account, false, statements)], warnings, stopwatch, cancellationToken);
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

        var file = await ImportFile.ReadAsync(fileStream, fileName, cancellationToken);
        if (await FindActiveBatchAsync(portfolioId, null, file.Sha256, cancellationToken) is { } previous)
            return AlreadyImported(previous, stopwatch.Elapsed);

        var parse = await ParseAsync(file, requestedSourceSystem, cancellationToken);
        if (parse.Failure is not null)
            return parse.Failure(stopwatch.Elapsed);
        var parsed = parse.File!;

        var targets = await RouteToPortfolioAccountsAsync(portfolioId, parsed, cancellationToken);
        if (targets.Count == 0)
            return PortfolioImportResult.FileHasNoAccountInfo(parsed.SourceSystem, stopwatch.Elapsed);

        var warnings = new ImportWarningCollector();
        warnings.AddRange(parsed.Warnings);
        var batch = file.ToBatch(portfolioId, accountId: null, parsed.SourceSystem);
        var result = await RunAsync(batch, parsed.SourceSystem, targets, warnings, stopwatch, cancellationToken);

        _logger.LogInformation(
            "Imported {Inserted} transactions across {AccountCount} accounts in portfolio {PortfolioId} from {Source}",
            result.Accounts.Sum(r => r.Inserted), result.Accounts.Count, portfolioId, parsed.SourceSystem);
        return result;
    }

    public async Task<ReprocessResult> ReprocessAsync(
        Guid? portfolioId, IReadOnlyCollection<Guid>? batchIds, CancellationToken cancellationToken)
    {
        var query = _db.ImportBatches.Where(b => b.Status == ImportBatchStatus.Active && b.Content != null);
        if (portfolioId is { } pid) query = query.Where(b => b.PortfolioId == pid);
        if (batchIds is not null) query = query.Where(b => batchIds.Contains(b.ImportBatchId));
        var batches = (await query.ToListAsync(cancellationToken)).OrderBy(b => b.ImportedAt).ToList();

        var perBatch = new List<ReprocessedBatch>(batches.Count);
        var affectedAccounts = new HashSet<Guid>();

        await _db.ExecuteInTransactionAsync(async () =>
        {
            // Saved per batch: each batch's dedup reads the ledger, which must include what earlier batches added.
            foreach (var batch in batches)
            {
                perBatch.Add(await ReprocessBatchAsync(batch, affectedAccounts, cancellationToken));
                await _db.SaveChangesAsync(cancellationToken);
            }

            await RelinkLedgerAsync(cancellationToken);
            foreach (var accountId in affectedAccounts)
                await _impliedContributions.SyncForAccountAsync(accountId, cancellationToken);
        }, cancellationToken);

        // Rows a parser used to drop may name holdings with no prices yet.
        if (perBatch.Any(b => b.Inserted > 0))
            _priceRefresh?.Enqueue(PriceRefreshRequest.ForAccounts(affectedAccounts, PriceRefreshTrigger.Import));

        _logger.LogInformation(
            "Reprocessed {Count} stored import files: {Inserted} rows added, {Updated} updated",
            perBatch.Count(b => b.Skipped is null), perBatch.Sum(b => b.Inserted), perBatch.Sum(b => b.Updated));

        return new ReprocessResult(
            perBatch.Count(b => b.Skipped is null),
            perBatch.Sum(b => b.Inserted),
            perBatch.Sum(b => b.Updated),
            perBatch.Sum(b => b.SnapshotsInserted),
            perBatch);
    }

    // ---------------- orchestration ----------------

    /// <summary>One account and the statements (from one file) that go into it.</summary>
    private sealed record ImportTarget(Account Account, bool Created, IReadOnlyList<ParsedAccountStatement> Statements);

    /// <summary>
    /// Records the batch and imports each target in one database transaction, then relinks holdings and re-derives
    /// implied contributions, and stores the result on the batch.
    /// </summary>
    private async Task<PortfolioImportResult> RunAsync(
        ImportBatch batch,
        string sourceSystem,
        IReadOnlyList<ImportTarget> targets,
        ImportWarningCollector warnings,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        _db.ImportBatches.Add(batch);
        foreach (var target in targets.Where(t => t.Created))
        {
            target.Account.MarkCreatedByImport(batch.ImportBatchId);
            _db.Accounts.Add(target.Account);
        }

        var currencies = await LoadCurrenciesAsync(cancellationToken);
        var results = new List<AccountImportResult>(targets.Count);

        await _db.ExecuteInTransactionAsync(async () =>
        {
            foreach (var target in targets)
                results.Add(await ImportStatementsAsync(target, sourceSystem, batch, currencies, warnings, cancellationToken));

            await _db.SaveChangesAsync(cancellationToken);
            await RelinkLedgerAsync(cancellationToken);
            for (var i = 0; i < results.Count; i++)
                results[i] = await WithImpliedContributionsAsync(results[i], cancellationToken);

            batch.RecordSummary(JsonSerializer.Serialize(
                new ImportBatchSummary(sourceSystem, results, warnings.ToList()), SummaryJson));
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        // Fetch prices for what was just imported in the background, so values appear without a manual step.
        _priceRefresh?.Enqueue(PriceRefreshRequest.ForAccounts(results.Select(r => r.AccountId), PriceRefreshTrigger.Import));

        stopwatch.Stop();
        return new PortfolioImportResult(PortfolioImportStatus.Success, sourceSystem, results, stopwatch.Elapsed)
        {
            ImportBatchId = batch.ImportBatchId,
            ImportedAt = batch.ImportedAt,
            Warnings = warnings.ToList(),
        };
    }

    private async Task<ReprocessedBatch> ReprocessBatchAsync(
        ImportBatch batch, HashSet<Guid> affectedAccounts, CancellationToken cancellationToken)
    {
        ReprocessedBatch Skip(string reason) => new(batch.ImportBatchId, batch.FileName, 0, 0, 0, reason);

        var parser = _parsers.FirstOrDefault(
            p => string.Equals(p.SourceSystem, batch.ParserSourceSystem, StringComparison.OrdinalIgnoreCase));
        if (parser is null) return Skip($"No parser for {batch.ParserSourceSystem} is registered any more.");

        var file = ImportFile.FromBatch(batch);
        ParsedPortfolioFile parsed;
        try
        {
            using var stream = new MemoryStream(file.Content, writable: false);
            parsed = await parser.ParseAsync(stream, batch.FileName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Reprocessing import {BatchId} ({FileName}) failed to parse", batch.ImportBatchId, batch.FileName);
            return Skip($"The file can no longer be read: {ex.Message}");
        }

        var warnings = new ImportWarningCollector();
        warnings.AddRange(parsed.Warnings);

        IReadOnlyList<ImportTarget> targets;
        if (batch.AccountId is { } accountId)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
            if (account is null) return Skip("The account it was imported into no longer exists.");
            var statements = RouteToAccount(account, parsed, warnings);
            if (statements is null) return Skip("The file no longer matches the account it was imported into.");
            targets = [new ImportTarget(account, false, statements)];
        }
        else
        {
            targets = await RouteToPortfolioAccountsAsync(batch.PortfolioId, parsed, cancellationToken);
            foreach (var target in targets.Where(t => t.Created))
            {
                target.Account.MarkCreatedByImport(batch.ImportBatchId);
                _db.Accounts.Add(target.Account);
            }
        }

        var currencies = await LoadCurrenciesAsync(cancellationToken);
        var results = new List<AccountImportResult>(targets.Count);
        foreach (var target in targets)
        {
            results.Add(await ImportStatementsAsync(target, parsed.SourceSystem, batch, currencies, warnings, cancellationToken));
            affectedAccounts.Add(target.Account.AccountId);
        }

        batch.RecordSummary(JsonSerializer.Serialize(MergeSummary(batch, parsed.SourceSystem, results, warnings), SummaryJson));
        batch.MarkReprocessed();

        return new ReprocessedBatch(
            batch.ImportBatchId, batch.FileName,
            results.Sum(r => r.Inserted), results.Sum(r => r.Updated), results.Sum(r => r.SnapshotsInserted), null);
    }

    /// <summary>
    /// The batch's summary after a reprocess: the original counts plus what the reprocess added or updated, and the
    /// warnings as the current parser sees the file.
    /// </summary>
    private static ImportBatchSummary MergeSummary(
        ImportBatch batch, string sourceSystem, List<AccountImportResult> reprocessed, ImportWarningCollector warnings)
    {
        var previous = ReadSummary(batch)?.Accounts ?? [];
        var merged = reprocessed
            .Select(r => previous.FirstOrDefault(p => p.AccountId == r.AccountId) is { } p
                ? p with
                {
                    Inserted = p.Inserted + r.Inserted,
                    Updated = p.Updated + r.Updated,
                    SnapshotsInserted = p.SnapshotsInserted + r.SnapshotsInserted,
                }
                : r)
            .ToList();
        return new ImportBatchSummary(sourceSystem, merged, warnings.ToList());
    }

    // ---------------- routing ----------------

    /// <summary>
    /// The statements of an account-scoped upload. A file with no account details (e.g. the Vanguard report) all
    /// goes to the account. A file that names accounts (e.g. a QFX) contributes only the statements for this
    /// account — matched by normalized account number — and the others are skipped with a warning. Null when the
    /// file names accounts and none is this one.
    /// </summary>
    private static IReadOnlyList<ParsedAccountStatement>? RouteToAccount(
        Account account, ParsedPortfolioFile parsed, ImportWarningCollector warnings)
    {
        if (!parsed.Statements.Any(HasAccountNumber)) return parsed.Statements;

        var key = Account.NormalizeAccountNumber(account.AccountNumber);
        var matching = parsed.Statements
            .Where(s => !HasAccountNumber(s) || Account.NormalizeAccountNumber(s.AccountNumber) == key)
            .ToList();
        if (!matching.Any(HasAccountNumber)) return null;

        foreach (var other in parsed.Statements.Except(matching))
            warnings.Add(ImportWarningCodes.OtherAccountSkipped,
                "The file also has statements for other accounts; they were skipped. Import it from the Accounts page to include them.",
                MaskAccountNumber(other.AccountNumber!));
        return matching;
    }

    /// <summary>
    /// The accounts a portfolio-scoped upload goes to: one per distinct (institution, normalized account number) in
    /// the file, found among the portfolio's accounts or created (unsaved). Statements for the same account share one
    /// import, so its holdings and snapshots are resolved once.
    /// </summary>
    private async Task<List<ImportTarget>> RouteToPortfolioAccountsAsync(
        Guid portfolioId, ParsedPortfolioFile parsed, CancellationToken cancellationToken)
    {
        var groups = parsed.Statements
            .Where(s => !string.IsNullOrWhiteSpace(s.InstitutionCode) && HasAccountNumber(s))
            .GroupBy(s => AccountKey(s.InstitutionCode!, s.AccountNumber!))
            .ToList();
        if (groups.Count == 0) return [];

        var existing = await _db.Accounts.Where(a => a.PortfolioId == portfolioId).ToListAsync(cancellationToken);
        var pending = _db.Accounts.Local.Where(a => a.PortfolioId == portfolioId && !existing.Contains(a));
        var byKey = existing.Concat(pending)
            .GroupBy(a => AccountKey(a.InstitutionCode, a.AccountNumber))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAt).First());

        var targets = new List<ImportTarget>(groups.Count);
        foreach (var group in groups)
        {
            var created = !byKey.TryGetValue(group.Key, out var account);
            if (created)
            {
                var first = group.First();
                account = Account.FromImport(portfolioId, first.InstitutionCode!, first.AccountNumber!);
                byKey[group.Key] = account;
            }

            targets.Add(new ImportTarget(account!, created, group.ToList()));
        }

        return targets;
    }

    private static bool HasAccountNumber(ParsedAccountStatement statement)
        => Account.NormalizeAccountNumber(statement.AccountNumber).Length > 0;

    private static string AccountKey(string institutionCode, string accountNumber) =>
        $"{institutionCode.Trim().ToLowerInvariant()}|{Account.NormalizeAccountNumber(accountNumber)}";

    /// <summary>"…1234": enough to recognise an account in a message without spelling out its number.</summary>
    private static string MaskAccountNumber(string accountNumber)
    {
        var normalized = Account.NormalizeAccountNumber(accountNumber);
        return normalized.Length <= 4 ? normalized : $"…{normalized[^4..]}";
    }

    // ---------------- one account ----------------

    private async Task<AccountImportResult> ImportStatementsAsync(
        ImportTarget target,
        string sourceSystem,
        ImportBatch batch,
        IReadOnlySet<string> validCurrencies,
        ImportWarningCollector warnings,
        CancellationToken cancellationToken)
    {
        var account = target.Account;
        var transactions = target.Statements.SelectMany(s => s.Transactions).ToList();
        var positions = target.Statements.SelectMany(s => s.Positions).ToList();

        var tickers = transactions.Select(t => t.Ticker)
            .Concat(positions.Select(p => p.Ticker))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        var resolver = new AccountHoldingResolver(_db, _logger);
        await resolver.PrimeAsync(account.AccountId, tickers, cancellationToken);

        var ledger = await LedgerMatcher.LoadAsync(_db, account.AccountId, sourceSystem, target.Created, cancellationToken);
        var failures = new List<PortfolioImportFailure>();
        var updates = new Dictionary<Guid, ImportedTransactionFields>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var inserted = 0;
        var skipped = 0;

        foreach (var (parsedTx, index) in transactions.Select((t, i) => (t, i)))
        {
            try
            {
                var fingerprint = TransactionFingerprint.Compute(account.AccountId, parsedTx);

                // Formats without row ids (e.g. the Vanguard report) get "{fingerprint}-{occurrence}": the n-th
                // identical row in the file. The same transaction then has the same id in every export of it — which
                // future corrections key on — while genuine same-day duplicates stay distinct.
                var occurrence = occurrences.GetValueOrDefault(fingerprint);
                occurrences[fingerprint] = occurrence + 1;
                var externalId = !string.IsNullOrWhiteSpace(parsedTx.ExternalId)
                    ? parsedTx.ExternalId!.Trim()
                    : $"{fingerprint}-{occurrence}";

                var match = ledger.Match(externalId, fingerprint);
                if (match.IsDuplicate)
                {
                    if (match.SameSourceRow is { } row && ImportedFieldsOf(parsedTx) is var next && row.Fields != next)
                        updates[row.Id] = next;
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
                entity.SetSplitRatio(parsedTx.SplitNumerator, parsedTx.SplitDenominator);
                entity.MarkSettlementFund(parsedTx.IsSettlementFund);
                entity.SetSubAccount(parsedTx.SubAccount);
                entity.TagImportBatch(batch.ImportBatchId);

                // Every row naming a security is linked — to an unclassified holding when reference data doesn't
                // know it yet — so its shares are always part of valuation rather than silently dropped.
                var holding = resolver.ResolveOrCreate(entity.Ticker, entity.Cusip, entity.CurrencyCode);
                if (holding is not null)
                {
                    entity.LinkToHolding(holding.AccountHoldingId);
                    if (parsedTx.IsSettlementFund) holding.MarkSettlementFund();
                }

                _db.AccountTransactions.Add(entity);
                ledger.Inserted(externalId);
                inserted++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                failures.Add(new PortfolioImportFailure(Key: $"row[{index}]", Reason: ex.Message));
            }
        }

        var snapshots = await ImportSnapshotsAsync(target, resolver, batch, validCurrencies, warnings, cancellationToken);
        await ApplyUpdatesAsync(updates, batch, cancellationToken);

        foreach (var holding in resolver.Created)
            holding.MarkCreatedByImport(batch.ImportBatchId);

        return new AccountImportResult(
            AccountId: account.AccountId,
            Created: target.Created,
            InstitutionCode: account.InstitutionCode,
            AccountNumber: account.AccountNumber,
            Considered: transactions.Count,
            Inserted: inserted,
            Skipped: skipped,
            Failed: failures.Count,
            Failures: failures)
        {
            Updated = updates.Count,
            SnapshotsInserted = snapshots,
        };
    }

    /// <summary>
    /// Brings rows already stored from this source in line with the current parser (Appendix A.11), recording the
    /// values before and after on the batch so undoing it can put them back.
    /// </summary>
    private async Task ApplyUpdatesAsync(
        Dictionary<Guid, ImportedTransactionFields> updates, ImportBatch batch, CancellationToken cancellationToken)
    {
        if (updates.Count == 0) return;

        var ids = updates.Keys.ToList();
        var rows = await _db.AccountTransactions
            .Where(t => ids.Contains(t.AccountTransactionId))
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var next = updates[row.AccountTransactionId];
            _db.ImportBatchRowUpdates.Add(new ImportBatchRowUpdate(batch.ImportBatchId, row, next));
            row.ApplyImportedFields(next);
        }
    }

    /// <summary>
    /// Records each statement position as a snapshot, plus the statement's available cash on the account's cash
    /// holding when it isn't already the settlement fund's position. Statements of one account share one set of
    /// holdings, so two statements in a file can't create the same snapshot twice.
    /// </summary>
    private async Task<int> ImportSnapshotsAsync(
        ImportTarget target,
        AccountHoldingResolver resolver,
        ImportBatch batch,
        IReadOnlySet<string> validCurrencies,
        ImportWarningCollector warnings,
        CancellationToken cancellationToken)
    {
        var accountId = target.Account.AccountId;
        var taken = target.Created
            ? new HashSet<(Guid, DateOnly)>()
            : (await _db.AccountHoldingSnapshots
                .Join(_db.AccountHoldings.Where(h => h.AccountId == accountId),
                    s => s.AccountHoldingId, h => h.AccountHoldingId, (s, _) => new { s.AccountHoldingId, s.AsOf })
                .ToListAsync(cancellationToken))
            .Select(s => (s.AccountHoldingId, s.AsOf))
            .ToHashSet();

        var inserted = 0;
        foreach (var pos in target.Statements.SelectMany(s => s.Positions))
        {
            var holding = resolver.ResolveOrCreate(pos.Ticker, pos.Cusip, pos.CurrencyCode);
            if (holding is null)
            {
                warnings.Add(ImportWarningCodes.PositionUnresolved,
                    "A statement position names no security (no ticker or CUSIP) and was not recorded.",
                    pos.AsOf.ToString("yyyy-MM-dd"));
                continue;
            }

            if (pos.IsSettlementFund) holding.MarkSettlementFund();
            if (!taken.Add((holding.AccountHoldingId, pos.AsOf))) continue;

            var snapshot = new AccountHoldingSnapshot(
                holding.AccountHoldingId, pos.AsOf, pos.Units, AccountHoldingSnapshotSource.BrokerPosition);
            snapshot.SetValuation(pos.CostBasis, pos.MarketValue, pos.UnitPrice, NormalizeCurrency(pos.CurrencyCode, validCurrencies));
            snapshot.SetPriceAsOf(pos.PriceAsOf);
            snapshot.TagImportBatch(batch.ImportBatchId);
            _db.AccountHoldingSnapshots.Add(snapshot);
            inserted++;
        }

        foreach (var cash in target.Statements.Select(s => s.Cash).OfType<ParsedCashBalance>())
        {
            if (cash.IncludesSettlementFund) continue;

            var (holding, created) = await CashHoldings.GetOrCreateAsync(_db, accountId, currencyCode: null, cancellationToken);
            if (created) holding.MarkCreatedByImport(batch.ImportBatchId);
            if (!taken.Add((holding.AccountHoldingId, cash.AsOf))) continue;

            var snapshot = new AccountHoldingSnapshot(
                holding.AccountHoldingId, cash.AsOf, cash.AvailableCash, AccountHoldingSnapshotSource.BrokerPosition);
            snapshot.SetValuation(costBasis: null, cash.AvailableCash, unitPrice: 1m, currencyCode: null);
            snapshot.TagImportBatch(batch.ImportBatchId);
            _db.AccountHoldingSnapshots.Add(snapshot);
            inserted++;
        }

        return inserted;
    }

    private static ImportedTransactionFields ImportedFieldsOf(ParsedTransaction tx) => new(
        tx.Type,
        tx.Amount,
        tx.Quantity,
        tx.Price,
        tx.SettlementDate,
        string.IsNullOrWhiteSpace(tx.SourceType) ? null : tx.SourceType.Trim(),
        tx.IsSettlementFund);

    // ---------------- after the import ----------------

    /// <summary>
    /// Links any rows still missing a holding (e.g. stored before every security row got one) and promotes
    /// unclassified holdings reference data now recognises, so each import also heals older data.
    /// </summary>
    private async Task RelinkLedgerAsync(CancellationToken cancellationToken)
    {
        if (_ledgerRelinker is not null)
            await _ledgerRelinker.RelinkAsync(cancellationToken);
    }

    /// <summary>
    /// Recomputes the account's implied contributions now that its imported rows are saved, and reports
    /// them on the result (see <see cref="IImpliedContributionService.SyncForAccountAsync"/>).
    /// </summary>
    private async Task<AccountImportResult> WithImpliedContributionsAsync(
        AccountImportResult result, CancellationToken cancellationToken)
    {
        var implied = await _impliedContributions.SyncForAccountAsync(result.AccountId, cancellationToken);
        return result with
        {
            ImpliedContributions = implied.Count,
            ImpliedContributionsAmount = implied.TotalAmount,
        };
    }

    // ---------------- files ----------------

    private async Task<ImportBatch?> FindActiveBatchAsync(
        Guid portfolioId, Guid? accountId, string sha256, CancellationToken cancellationToken)
    {
        // Ordered in memory: SQLite can't order by DateTimeOffset, and there's at most a handful of matches.
        var matches = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.PortfolioId == portfolioId && b.AccountId == accountId && b.FileSha256 == sha256
                        && b.Status == ImportBatchStatus.Active)
            .ToListAsync(cancellationToken);
        return matches.MaxBy(b => b.ImportedAt);
    }

    /// <summary>The earlier import of the same file, repeated: nothing is written.</summary>
    private static PortfolioImportResult AlreadyImported(ImportBatch previous, TimeSpan elapsed)
    {
        var summary = ReadSummary(previous);
        return new PortfolioImportResult(
            PortfolioImportStatus.AlreadyImported,
            previous.ParserSourceSystem,
            summary?.Accounts ?? [],
            elapsed)
        {
            ImportBatchId = previous.ImportBatchId,
            ImportedAt = previous.ImportedAt,
            Warnings = summary?.Warnings ?? [],
        };
    }

    internal static ImportBatchSummary? ReadSummary(ImportBatch batch) => ReadSummary(batch.SummaryJson);

    internal static ImportBatchSummary? ReadSummary(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<ImportBatchSummary>(summaryJson, SummaryJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ParseOutcome(ParsedPortfolioFile? File, Func<TimeSpan, PortfolioImportResult>? Failure);

    private async Task<ParseOutcome> ParseAsync(ImportFile file, string? requestedSourceSystem, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(file.Content, writable: false);

        // Explicit override from the UI: trust the caller's pick and skip auto-detection.
        if (!string.IsNullOrWhiteSpace(requestedSourceSystem))
        {
            var requested = requestedSourceSystem.Trim();
            var forced = _parsers.FirstOrDefault(
                p => string.Equals(p.SourceSystem, requested, StringComparison.OrdinalIgnoreCase));
            if (forced is null)
            {
                _logger.LogWarning("No parser registered for requested source system {SourceSystem}", requested);
                return new ParseOutcome(null, d => PortfolioImportResult.UnknownParser(requested, d));
            }

            return new ParseOutcome(await forced.ParseAsync(buffer, file.FileName, cancellationToken), null);
        }

        // Auto-detect: offer the file to parsers from highest priority to lowest so a
        // provider-specific parser gets first refusal ahead of any generic fallback.
        foreach (var parser in _parsers.OrderByDescending(p => p.Priority))
        {
            buffer.Position = 0;
            if (!await parser.CanParseAsync(buffer, file.FileName, cancellationToken)) continue;

            buffer.Position = 0;
            return new ParseOutcome(await parser.ParseAsync(buffer, file.FileName, cancellationToken), null);
        }

        _logger.LogWarning("No parser matched uploaded file {FileName}", file.FileName);
        return new ParseOutcome(null, PortfolioImportResult.UnsupportedFormat);
    }

    private async Task<HashSet<string>> LoadCurrenciesAsync(CancellationToken cancellationToken)
    {
        var codes = await _db.Currencies.AsNoTracking()
            .Select(c => c.Code).ToListAsync(cancellationToken);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    private static string? NormalizeCurrency(string? raw, IReadOnlySet<string> validCodes)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToUpperInvariant();
        return validCodes.Contains(normalized) ? normalized : null;
    }
}
