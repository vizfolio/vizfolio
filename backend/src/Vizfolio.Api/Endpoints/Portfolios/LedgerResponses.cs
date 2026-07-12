namespace Vizfolio.Api.Endpoints.Portfolios;

/// <summary>
/// A single transaction in an account's ledger. <see cref="Type"/> is the normalized transaction
/// type; <see cref="SourceType"/> preserves the broker's original label when richer. When the
/// transaction is linked to a holding, <see cref="HoldingName"/> carries that holding's name.
/// </summary>
public sealed record LedgerEntryResponse(
    Guid AccountTransactionId,
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    string Type,
    string? SourceType,
    string? Ticker,
    string? Cusip,
    Guid? AccountHoldingId,
    string? HoldingName,
    decimal? Quantity,
    decimal? Price,
    decimal Amount,
    decimal? Fees,
    string? CurrencyCode,
    string? Memo);
