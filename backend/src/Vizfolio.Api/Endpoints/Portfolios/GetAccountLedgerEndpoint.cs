using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application.Abstractions;

namespace Vizfolio.Api.Endpoints.Portfolios;

public sealed record GetAccountLedgerRequest(
    Guid PortfolioId,
    Guid AccountId,
    DateOnly? From,
    DateOnly? To);

/// <summary>
/// Lists an account's transactions (its ledger), newest first, optionally filtered to a trade-date
/// range. Returns everything in range; sorting and further filtering happen client-side.
/// </summary>
public sealed class GetAccountLedgerEndpoint
    : Endpoint<GetAccountLedgerRequest, IReadOnlyList<LedgerEntryResponse>>
{
    private readonly IAppDbContext _db;

    public GetAccountLedgerEndpoint(IAppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/portfolios/{portfolioId}/accounts/{accountId}/ledger");
        AllowAnonymous();
        Description(b => b
            .WithTags("Portfolios")
            .Produces<IReadOnlyList<LedgerEntryResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound));
        Summary(s =>
        {
            s.Summary = "List an account's transaction ledger.";
            s.Params["from"] = "Earliest trade date to include (inclusive). Defaults to the account's first transaction.";
            s.Params["to"] = "Latest trade date to include (inclusive). Defaults to today (UTC).";
        });
    }

    public override async Task HandleAsync(GetAccountLedgerRequest req, CancellationToken ct)
    {
        if (req.From.HasValue && req.To.HasValue && req.From.Value > req.To.Value)
        {
            await Send.ResponseAsync(default!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var accountInScope = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PortfolioId == req.PortfolioId && a.AccountId == req.AccountId, ct);
        if (!accountInScope)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var query = _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountId == req.AccountId);
        if (req.From.HasValue) query = query.Where(t => t.TradeDate >= req.From.Value);
        if (req.To.HasValue) query = query.Where(t => t.TradeDate <= req.To.Value);

        // Order newest-first in memory: SQLite can't ORDER BY the DateTimeOffset tiebreak, and
        // keeping the sort provider-agnostic avoids leaning on any one database's capabilities.
        var transactions = (await query.ToListAsync(ct))
            .OrderByDescending(t => t.TradeDate)
            .ThenByDescending(t => t.ImportedAt)
            .ToList();

        // Look up the display name for any linked holdings so each row can carry it.
        var holdingNames = await _db.AccountHoldings
            .AsNoTracking()
            .Where(h => h.AccountId == req.AccountId)
            .Select(h => new { h.AccountHoldingId, h.Name })
            .ToDictionaryAsync(h => h.AccountHoldingId, h => h.Name, ct);

        var results = transactions
            .Select(t => new LedgerEntryResponse(
                t.AccountTransactionId,
                t.TradeDate,
                t.SettlementDate,
                t.Type.ToString(),
                t.SourceType,
                t.Ticker,
                t.Cusip,
                t.AccountHoldingId,
                t.AccountHoldingId is { } hid && holdingNames.TryGetValue(hid, out var name) ? name : null,
                t.Quantity,
                t.Price,
                t.Amount,
                t.Fees,
                t.CurrencyCode,
                t.Memo))
            .ToList();

        await Send.OkAsync(results, ct);
    }
}
