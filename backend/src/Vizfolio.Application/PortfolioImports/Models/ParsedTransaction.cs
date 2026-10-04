using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.PortfolioImports.Models;

public sealed record ParsedTransaction(
    string? ExternalId,
    TransactionType Type,
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    string? Ticker,
    string? Cusip,
    decimal? Quantity,
    decimal? Price,
    decimal Amount,
    decimal? Fees,
    string? CurrencyCode,
    string? Memo,
    // The broker's raw type label, preserved verbatim (see AccountTransaction.SourceType).
    // Trailing optional fields with defaults so existing parsers/tests compile unchanged.
    string? SourceType = null,
    // A movement of the account's settlement (core / sweep) fund, as the broker or format marks it.
    bool IsSettlementFund = false,
    // A split's ratio (new : old shares), on Split rows.
    decimal? SplitNumerator = null,
    decimal? SplitDenominator = null,
    // The broker's sub-account (e.g. OFX SUBACCTSEC "CASH"/"MARGIN"); metadata only.
    string? SubAccount = null);
