using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.PortfolioImports.Services;
using Vizfolio.Application.Portfolios.Valuation;
using Vizfolio.Application.Pricing.Abstractions;
using Vizfolio.Domain.Portfolios;
using Vizfolio.Domain.Pricing;

namespace Vizfolio.Application.Portfolios.Health;

/// <summary>
/// Turns what the valuation engine already knows into findings a person can act on (roadmap 6.1): statements the
/// ledger disagrees with, positions it can't explain, days a holding can't be valued and why, transfers without a
/// price, rows the parsers didn't understand, and contributions Vizfolio had to infer. Builds each account's engine
/// once — the same one performance uses — and reads the import records; it never writes.
/// </summary>
public sealed class DataHealthService : IDataHealthService
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly IAppDbContext _db;
    private readonly AccountValuationLoader _loader;
    private readonly IPriceRefreshStatus? _priceRefresh;
    private readonly TimeProvider _time;

    public DataHealthService(
        IAppDbContext db,
        AccountValuationLoader loader,
        IPriceRefreshStatus? priceRefresh = null,
        TimeProvider? time = null)
    {
        _db = db;
        _loader = loader;
        _priceRefresh = priceRefresh;
        _time = time ?? TimeProvider.System;
    }

    public async Task<DataHealthReport?> GetForPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        if (!await _db.Portfolios.AsNoTracking().AnyAsync(p => p.PortfolioId == portfolioId, cancellationToken))
            return null;

        var accounts = await _db.Accounts.AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId)
            .Select(a => new AccountRef(a.AccountId, a.Name))
            .ToListAsync(cancellationToken);
        return await BuildAsync(portfolioId, accounts, cancellationToken);
    }

    public async Task<DataHealthReport?> GetForAccountAsync(Guid portfolioId, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts.AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId && a.AccountId == accountId)
            .Select(a => new AccountRef(a.AccountId, a.Name))
            .FirstOrDefaultAsync(cancellationToken);
        return account is null ? null : await BuildAsync(portfolioId, [account], cancellationToken);
    }

    private sealed record AccountRef(Guid AccountId, string Name);

    private async Task<DataHealthReport> BuildAsync(
        Guid portfolioId, IReadOnlyList<AccountRef> accounts, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var ids = accounts.Select(a => a.AccountId).ToList();
        var findings = new List<HealthFinding>();
        var currency = AccountValuationLoader.DefaultCurrencyCode;

        if (ids.Count > 0)
        {
            var valuation = await _loader.LoadAsync(ids, today, cancellationToken);
            currency = valuation.ReportingCurrency;
            var context = await LoadContextAsync(ids, cancellationToken);
            foreach (var (accountId, engine) in valuation.Engines)
                findings.AddRange(EngineFindings(accountId, engine, today, valuation.ReportingCurrency, context));

            findings.AddRange(await ImpliedContributionFindingsAsync(ids, valuation.ReportingCurrency, cancellationToken));
            findings.AddRange(await ImportWarningFindingsAsync(portfolioId, ids, cancellationToken));
        }

        var position = accounts.Select((a, i) => (a.AccountId, i)).ToDictionary(x => x.AccountId, x => x.i);
        var ordered = findings
            .OrderBy(f => f.Severity == HealthSeverities.Blocking ? 0 : 1)
            .ThenBy(f => f.AccountId is { } id && position.TryGetValue(id, out var i) ? i : int.MaxValue)
            .ThenBy(f => f.From ?? DateOnly.MaxValue)
            .ThenBy(f => f.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var perAccount = accounts.Select(a =>
        {
            var own = ordered.Where(f => f.AccountId == a.AccountId).ToList();
            var blocking = own.Count(f => f.Severity == HealthSeverities.Blocking);
            return new AccountHealth(a.AccountId, a.Name, StatusOf(own), blocking, own.Count - blocking);
        }).ToList();

        return new DataHealthReport(StatusOf(ordered), currency, perAccount, ordered);
    }

    private static string StatusOf(IReadOnlyCollection<HealthFinding> findings) =>
        findings.Any(f => f.Severity == HealthSeverities.Blocking) ? HealthStatuses.NeedsAttention
        : findings.Count > 0 ? HealthStatuses.Info
        : HealthStatuses.Healthy;

    // ---------------- the engine's view ----------------

    private sealed record EngineContext(
        IReadOnlyDictionary<Guid, PriceSeriesStatus> SeriesByHolding,
        IReadOnlyDictionary<Guid, (string? Ticker, decimal? Quantity)> TransferRows);

    private async Task<EngineContext> LoadContextAsync(List<Guid> accountIds, CancellationToken cancellationToken)
    {
        var holdings = await _db.AccountHoldings.AsNoTracking()
            .Where(h => accountIds.Contains(h.AccountId))
            .Select(h => new { h.AccountHoldingId, h.SecurityId, h.Symbol })
            .ToListAsync(cancellationToken);
        var statuses = await _db.PriceSeriesStatuses.AsNoTracking().ToListAsync(cancellationToken);

        // Keyed the way price series are: by security when the holding has one, else by upper-cased symbol.
        var seriesByHolding = new Dictionary<Guid, PriceSeriesStatus>();
        foreach (var h in holdings)
        {
            var symbolKey = h.Symbol?.Trim().ToUpperInvariant();
            var status = h.SecurityId is { } sid
                ? statuses.FirstOrDefault(s => s.Kind == PriceSeriesKind.Security && s.SecurityId == sid)
                : statuses.FirstOrDefault(s => s.Kind == PriceSeriesKind.Symbol && s.SymbolKey == symbolKey);
            if (status is not null) seriesByHolding[h.AccountHoldingId] = status;
        }

        var transfers = await _db.AccountTransactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.Type == TransactionType.Transfer && t.Quantity != null)
            .Select(t => new { t.AccountTransactionId, t.Ticker, t.Quantity })
            .ToListAsync(cancellationToken);

        return new EngineContext(
            seriesByHolding,
            transfers.ToDictionary(t => t.AccountTransactionId, t => (t.Ticker, t.Quantity)));
    }

    private IEnumerable<HealthFinding> EngineFindings(
        Guid accountId, AccountStateEngine engine, DateOnly today, string currency, EngineContext context)
    {
        foreach (var finding in engine.Findings)
            yield return FromEngineFinding(accountId, finding, currency);

        if (engine.OpeningDate is not { } opening) yield break;

        var pending = _priceRefresh?.IsPending(accountId) ?? false;
        foreach (var gap in engine.MissingIntervals(opening, today))
            if (FromMissingInterval(accountId, gap, pending, context) is { } finding)
                yield return finding;

        foreach (var flow in engine.FlowsBetween(opening, today).Where(f => !f.IsValued))
        {
            context.TransferRows.TryGetValue(flow.TransactionId, out var row);
            var what = row.Quantity is { } q ? $"{Shares(Math.Abs(q))} {row.Ticker ?? "shares"}" : "shares";
            yield return new HealthFinding(
                HealthCodes.UnvaluedTransfer, HealthSeverities.Blocking, accountId, null, row.Ticker,
                flow.Date, flow.Date,
                $"A transfer of {what} on {Day(flow.Date)} has no price, so returns over that date can't be computed.",
                new HealthAction(HealthActionKinds.FetchPrices, "Fetch prices"),
                HealthDetails.Empty with { BrokerQuantity = row.Quantity, PricesPending = pending });
        }
    }

    private static HealthFinding FromEngineFinding(Guid accountId, ReconciliationFinding f, string currency)
    {
        var details = new HealthDetails
        {
            LedgerQuantity = f.LedgerQuantity,
            BrokerQuantity = f.BrokerQuantity,
            Amount = f.DifferenceValue,
        };
        var reimport = new HealthAction(HealthActionKinds.ReimportFile, "Import a file covering this period");

        return f.Code switch
        {
            FindingCode.QuantityMismatch => f.IsMaterial
                ? new HealthFinding(HealthCodes.QuantityMismatch, HealthSeverities.Blocking, accountId, f.HoldingId, f.Symbol, null, f.Date,
                    $"Your history doesn't match your broker's statement for {f.Symbol} on {Day(f.Date)}: the transactions add up to " +
                    $"{Shares(f.LedgerQuantity)} shares, the statement says {Shares(f.BrokerQuantity)}. Its value before that statement is left blank.",
                    reimport, details)
                : new HealthFinding(HealthCodes.QuantityMismatch, HealthSeverities.Info, accountId, f.HoldingId, f.Symbol, null, f.Date,
                    $"A small difference for {f.Symbol} on {Day(f.Date)} ({Shares(f.LedgerQuantity)} vs {Shares(f.BrokerQuantity)} shares" +
                    $"{AboutMoney(f.DifferenceValue, currency)}); the statement's figure is used from then on.",
                    HealthAction.None, details),
            FindingCode.CashMismatch => f.IsMaterial
                ? new HealthFinding(HealthCodes.CashMismatch, HealthSeverities.Blocking, accountId, null, null, null, f.Date,
                    $"Cash doesn't match your broker's statement on {Day(f.Date)}: the transactions add up to {Money(f.LedgerQuantity, currency)}, " +
                    $"the statement says {Money(f.BrokerQuantity, currency)}. Cash before that statement is left blank.",
                    reimport, details)
                : new HealthFinding(HealthCodes.CashMismatch, HealthSeverities.Info, accountId, null, null, null, f.Date,
                    $"Cash differs slightly from your broker's statement on {Day(f.Date)} ({Money(f.LedgerQuantity, currency)} vs " +
                    $"{Money(f.BrokerQuantity, currency)}); the statement's figure is used from then on.",
                    HealthAction.None, details),
            FindingCode.NegativePosition => new HealthFinding(
                HealthCodes.NegativePosition, HealthSeverities.Blocking, accountId, f.HoldingId, f.Symbol, f.Date, null,
                $"Your history sells or transfers out more {f.Symbol} than it bought, from {Day(f.Date)} on. A transaction is probably " +
                "missing — import a file that covers it.",
                reimport, details),
            FindingCode.UnmatchedSplit => new HealthFinding(
                HealthCodes.UnmatchedSplit, HealthSeverities.Info, accountId, f.HoldingId, f.Symbol, f.Date, f.Date,
                $"Your file records a {f.Symbol} split on {Day(f.Date)} that price data doesn't show; it was applied as the file reports it.",
                HealthAction.None, details),
            FindingCode.PreHistoryPosition => new HealthFinding(
                HealthCodes.PreHistoryPosition, HealthSeverities.Info, accountId, f.HoldingId, f.Symbol, null, f.Date,
                $"We assumed you held {Shares(f.BrokerQuantity)} {f.Symbol} before your first imported transaction, from your broker's statement.",
                new HealthAction(HealthActionKinds.AdjustStartingPosition, "Adjust starting positions"), details),
            _ => throw new ArgumentOutOfRangeException(nameof(f), f.Code, null),
        };
    }

    /// <summary>
    /// A stretch without a price. Negative positions and material mismatches already have their own finding, so
    /// only price gaps become one here.
    /// </summary>
    private static HealthFinding? FromMissingInterval(Guid accountId, MissingInterval gap, bool pending, EngineContext context)
    {
        if (gap.Cause is not (MissingCause.NoPrice or MissingCause.StalePrice)) return null;

        var series = gap.HoldingId is { } id && context.SeriesByHolding.TryGetValue(id, out var s) ? s : null;
        var details = HealthDetails.Empty with
        {
            PricesPending = pending,
            PriceFetchOutcome = series?.LastOutcome.ToString(),
            PriceFetchMessage = series?.Message,
        };
        var code = gap.Cause == MissingCause.NoPrice ? HealthCodes.UnpricedHolding : HealthCodes.StalePrice;
        var symbol = gap.Symbol ?? "a holding";
        var when = gap.From == gap.To ? $"on {Day(gap.From)}" : $"from {Day(gap.From)} to {Day(gap.To)}";

        if (pending)
            return new HealthFinding(code, HealthSeverities.Info, accountId, gap.HoldingId, gap.Symbol, gap.From, gap.To,
                $"Prices for {symbol} are still downloading ({when} is not valued yet).", HealthAction.None, details);

        var noProvider = series?.LastOutcome == PriceFetchOutcome.NoSource;
        var action = noProvider
            ? new HealthAction(HealthActionKinds.AddPriceProviderKey, "Add a price provider")
            : new HealthAction(HealthActionKinds.FetchPrices, "Fetch prices");
        var message = gap.Cause == MissingCause.NoPrice
            ? $"No price for {symbol} {when}, so the account's value is unknown on those days."
            : $"The latest price for {symbol} is too old {when}, so the account's value is unknown on those days.";
        if (noProvider) message += " No configured price provider has it.";
        return new HealthFinding(code, HealthSeverities.Blocking, accountId, gap.HoldingId, gap.Symbol, gap.From, gap.To,
            message, action, details);
    }

    // ---------------- stored records ----------------

    private async Task<IEnumerable<HealthFinding>> ImpliedContributionFindingsAsync(
        List<Guid> accountIds, string currency, CancellationToken cancellationToken)
    {
        var rows = await _db.AccountTransactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.SourceSystem == ImpliedContributionService.SourceSystem)
            .Select(t => new { t.AccountId, t.TradeDate, t.Amount })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.AccountId).Select(g =>
        {
            var first = g.Min(r => r.TradeDate);
            var last = g.Max(r => r.TradeDate);
            var total = g.Sum(r => Math.Abs(r.Amount));
            var years = first.Year == last.Year ? $"{first.Year}" : $"{first.Year}–{last.Year}";
            var (noun, them) = g.Count() == 1 ? ("purchase had", "it") : ("purchases had", "them");
            return new HealthFinding(
                HealthCodes.ImpliedContributions, HealthSeverities.Info, g.Key, null, null, first, last,
                $"{g.Count()} {noun} no recorded deposit ({years}), so {Money(total, currency)} of contributions were added for {them} " +
                "rather than counting the money as investment gains.",
                new HealthAction(HealthActionKinds.ReviewImpliedContributions, "Review"),
                HealthDetails.Empty with { Count = g.Count(), Amount = total });
        }).ToList();
    }

    /// <summary>
    /// What the parsers reported but couldn't fully understand, from the active imports' records (kept up to date by
    /// reprocessing). Grouped by warning and account; a file that went into several accounts reports once, unattributed.
    /// </summary>
    private async Task<IEnumerable<HealthFinding>> ImportWarningFindingsAsync(
        Guid portfolioId, List<Guid> accountIds, CancellationToken cancellationToken)
    {
        var batches = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.PortfolioId == portfolioId && b.Status == ImportBatchStatus.Active && b.SummaryJson != null)
            .Select(b => new { b.FileName, b.SummaryJson })
            .ToListAsync(cancellationToken);

        var warnings = new List<(Guid? AccountId, string File, ImportWarning Warning)>();
        foreach (var batch in batches)
        {
            if (PortfolioImportService.ReadSummary(batch.SummaryJson) is not { } summary) continue;
            var touched = summary.Accounts.Select(a => a.AccountId).Distinct().ToList();
            if (!touched.Any(accountIds.Contains)) continue;

            Guid? owner = touched.Count == 1 ? touched[0] : null;
            if (owner is null && accountIds.Count == 1) owner = accountIds[0];
            foreach (var warning in summary.Warnings.Where(w => w.Code != ImportWarningCodes.OtherAccountSkipped))
                warnings.Add((owner, batch.FileName, warning));
        }

        return warnings.GroupBy(w => (w.AccountId, w.Warning.Code)).Select(g =>
        {
            var count = g.Sum(w => w.Warning.Count);
            return new HealthFinding(
                HealthCodes.ImportWarning, HealthSeverities.Info, g.Key.AccountId, null, null, null, null,
                $"{g.First().Warning.Message} ({count} {(count == 1 ? "row" : "rows")})",
                HealthAction.None,
                HealthDetails.Empty with
                {
                    WarningCode = g.Key.Code,
                    Count = count,
                    Samples = g.SelectMany(w => w.Warning.Samples).Distinct().Take(5).ToList(),
                    Files = g.Select(w => w.File).Distinct().ToList(),
                });
        }).ToList();
    }

    // ---------------- wording ----------------

    private static string Day(DateOnly date) => date.ToString("MMM d, yyyy", Invariant);

    private static string Shares(decimal? quantity) =>
        quantity is { } q ? q.ToString("#,0.####", Invariant) : "?";

    private static string Money(decimal? amount, string currency)
    {
        if (amount is not { } a) return "?";
        var text = Math.Abs(a).ToString("#,0.00", Invariant);
        var sign = a < 0 ? "-" : "";
        return currency == "USD" ? $"{sign}${text}" : $"{sign}{text} {currency}";
    }

    private static string AboutMoney(decimal? amount, string currency) =>
        amount is { } a ? $", about {Money(a, currency)}" : "";
}
