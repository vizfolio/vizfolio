using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;
using Vizfolio.Application.PortfolioImports.Models;
using Vizfolio.Application.Portfolios;
using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Services;

/// <summary>The evidence for where a file's rows belong: one <see cref="RoutingCandidate"/> per account, best first.</summary>
public sealed record RoutingAnalysis(
    int FileRows, DateOnly? FirstDate, DateOnly? LastDate, IReadOnlyList<RoutingCandidate> Candidates);

/// <summary>
/// Works out which account a file belongs to when the file doesn't say (roadmap 2.6). Each of the file's rows is
/// fingerprinted the way deduplication does it (<see cref="TransactionFingerprint"/>) and counted against each
/// account's stored rows: an account that already holds many of them is where the file came from. On real
/// multi-account data a re-export matches its own account on every row and any other account on a couple at most,
/// because the fingerprint includes the exact date, share count and amount.
/// </summary>
internal sealed class AccountRoutingAnalyzer
{
    private readonly IAppDbContext _db;

    public AccountRoutingAnalyzer(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<RoutingAnalysis> AnalyzeAsync(
        IReadOnlyList<ParsedTransaction> rows, IReadOnlyList<Account> candidates, CancellationToken cancellationToken)
    {
        var ids = candidates.Select(a => a.AccountId).ToList();
        var stored = ids.Count == 0
            ? []
            : await _db.AccountTransactions.AsNoTracking()
                .Where(t => ids.Contains(t.AccountId) && t.SourceSystem != ImpliedContributionService.SourceSystem)
                .Select(t => new StoredRow(t.AccountId, t.TradeDate, t.Ticker, t.Quantity, t.Amount))
                .ToListAsync(cancellationToken);
        var byAccount = stored.ToLookup(r => r.AccountId);
        var fileTickers = Tickers(rows.Select(r => r.Ticker));

        var ranked = candidates
            .Select(account => Score(account, rows, byAccount[account.AccountId].ToList(), fileTickers))
            .OrderByDescending(c => c.MatchingRows)
            .ThenByDescending(c => c.SharedTickers)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RoutingAnalysis(
            rows.Count,
            rows.Count == 0 ? null : rows.Min(r => r.TradeDate),
            rows.Count == 0 ? null : rows.Max(r => r.TradeDate),
            ranked);
    }

    /// <summary>
    /// The account the evidence clearly points to, or null when it doesn't: the best candidate holds at least
    /// <see cref="ImportRoutingOptions.MinMatchingRows"/> of the file's rows, at least
    /// <see cref="ImportRoutingOptions.MinLeadRatio"/> times as many as the runner-up, and at least
    /// <see cref="ImportRoutingOptions.MinOverlapShare"/> of the file's rows dated within its history.
    /// </summary>
    public static Guid? StrongMatch(RoutingAnalysis analysis, ImportRoutingOptions options)
    {
        if (analysis.Candidates.Count == 0) return null;

        var best = analysis.Candidates[0];
        var runnerUp = analysis.Candidates.Count > 1 ? analysis.Candidates[1].MatchingRows : 0;

        var enough = best.MatchingRows >= Math.Max(1, options.MinMatchingRows);
        var leads = best.MatchingRows >= options.MinLeadRatio * runnerUp && best.MatchingRows > runnerUp;
        var consistent = best.RowsInAccountRange > 0
            && best.MatchingRows >= options.MinOverlapShare * best.RowsInAccountRange;
        return enough && leads && consistent ? best.AccountId : null;
    }

    private sealed record StoredRow(Guid AccountId, DateOnly TradeDate, string? Ticker, decimal? Quantity, decimal Amount);

    private static RoutingCandidate Score(
        Account account, IReadOnlyList<ParsedTransaction> rows, IReadOnlyList<StoredRow> stored, HashSet<string> fileTickers)
    {
        var storedCounts = FingerprintMultiset.Count(stored.Select(
            r => TransactionFingerprint.Compute(account.AccountId, r.TradeDate, r.Ticker, r.Quantity, r.Amount)));
        var fileCounts = FingerprintMultiset.Count(rows.Select(r => TransactionFingerprint.Compute(account.AccountId, r)));

        var inRange = 0;
        if (stored.Count > 0)
        {
            var first = stored.Min(r => r.TradeDate);
            var last = stored.Max(r => r.TradeDate);
            inRange = rows.Count(r => r.TradeDate >= first && r.TradeDate <= last);
        }

        var shared = Tickers(stored.Select(r => r.Ticker));
        shared.IntersectWith(fileTickers);

        return new RoutingCandidate(
            account.AccountId,
            account.Name,
            account.InstitutionCode,
            MaskAccountNumber(account.AccountNumber),
            FingerprintMultiset.Overlap(fileCounts, storedCounts),
            inRange,
            shared.Count);
    }

    private static HashSet<string> Tickers(IEnumerable<string?> tickers) =>
        tickers.Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>"…1234": enough to recognise an account in a message without spelling out its number.</summary>
    internal static string MaskAccountNumber(string? accountNumber)
    {
        var normalized = Account.NormalizeAccountNumber(accountNumber);
        return normalized.Length <= 4 ? normalized : $"…{normalized[^4..]}";
    }
}
