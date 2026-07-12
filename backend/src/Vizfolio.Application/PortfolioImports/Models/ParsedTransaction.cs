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
    // Trailing with a default so existing parsers/tests compile unchanged.
    string? SourceType = null);
