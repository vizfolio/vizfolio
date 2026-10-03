using Vizfolio.Domain.Portfolios;

namespace Vizfolio.Application.Portfolios.Valuation;

// ---------- inputs (built by AccountValuationLoader; the engine itself does no I/O) ----------

/// <summary>One ledger row as the valuation engine sees it.</summary>
public sealed record LedgerRow(
    Guid TransactionId,
    string SourceSystem,
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    TransactionType Type,
    Guid? HoldingId,
    string? Ticker,
    decimal? Quantity,
    decimal Amount,
    decimal? Price,
    string? SourceType)
{
    /// <summary>A contribution Vizfolio derived itself (see ImpliedContributionService), not an imported row.</summary>
    public bool IsImplied => string.Equals(SourceSystem, ImpliedContributionService.SourceSystem, StringComparison.Ordinal);

    /// <summary>A broker sweep between cash and the settlement fund (labelled "Sweep…"), whatever its ticker.</summary>
    public bool IsSweep => SourceType?.TrimStart().StartsWith("sweep", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Where a position or cash anchor came from.</summary>
public enum AnchorSource
{
    BrokerPosition,
    Statement,
    OpeningBalance,

    /// <summary>Rolled back from the earliest broker anchor to the day before the account's first transaction.</summary>
    Derived,
}

/// <summary>A known end-of-day position: a stored snapshot, or a derived opening.</summary>
public readonly record struct PositionAnchor(DateOnly AsOf, decimal Quantity, decimal? MarketValue, AnchorSource Source)
{
    /// <summary>Per-share value recorded by the anchor, when it records both a position and a value.</summary>
    public decimal? UnitPrice => Quantity != 0m && MarketValue is { } mv ? mv / Quantity : null;
}

/// <summary>A provider split: shares held at the close of the day before <see cref="ExDate"/> are multiplied by <see cref="Factor"/>.</summary>
public readonly record struct SplitAction(DateOnly ExDate, decimal Factor);

/// <summary>A holding's valuation inputs. Anchors are ascending by date.</summary>
public sealed record HoldingInput(
    Guid HoldingId,
    string? Symbol,
    AccountHoldingKind Kind,
    PriceSeries Prices,
    IReadOnlyList<SplitAction> Splits,
    IReadOnlyList<PositionAnchor> AnchorsAscending);

/// <summary>Everything the engine needs to value one account at any date.</summary>
public sealed record AccountValuationInput(
    Guid AccountId,
    IReadOnlyList<LedgerRow> Ledger,
    IReadOnlyList<HoldingInput> Holdings,
    ValuationOptions Options);

// ---------- outputs ----------

/// <summary>Why a held component couldn't be valued.</summary>
public enum MissingCause
{
    /// <summary>No price (or snapshot) on or near the date.</summary>
    NoPrice,

    /// <summary>The only price is older than <see cref="ValuationOptions.MaxPriceAgeDays"/>.</summary>
    StalePrice,

    /// <summary>The ledger sells or transfers out more shares than it holds — history is missing rows.</summary>
    NegativePosition,

    /// <summary>The ledger disagrees materially with the broker's next statement, so it can't be trusted here.</summary>
    MaterialMismatch,

    /// <summary>The date is before the account's imported history, and a position was already held then.</summary>
    BeforeHistory,
}

/// <summary>Where a component's value came from.</summary>
public enum ComponentSource
{
    Price,
    Snapshot,
    StableNav,
    Cash,
}

/// <summary>One holding's (or, with a null <see cref="HoldingId"/>, the account's cash) value on a date.</summary>
public sealed record ComponentValue(
    Guid? HoldingId,
    string? Symbol,
    decimal Quantity,
    decimal Value,
    HoldingValuationStatus Status,
    ComponentSource? Source,
    MissingCause? Cause,
    decimal? UnitPrice,
    DateOnly? PriceAsOf,
    DateOnly? SnapshotAsOf)
{
    public bool IsCash => HoldingId is null;
}

public enum HoldingValuationStatus
{
    /// <summary>Value is known.</summary>
    Covered,

    /// <summary>Not held on the date — a true $0, counted as complete.</summary>
    NotHeld,

    /// <summary>Held but no usable valuation — an honest "unknown".</summary>
    Missing,
}

/// <summary>An account's value on a date: the sum of its components, complete only if none is missing.</summary>
public sealed record AccountValue(DateOnly Date, decimal Value, bool IsComplete, IReadOnlyList<ComponentValue> Components);

public enum FlowKind
{
    Deposit,
    Withdrawal,
    CashTransfer,
    InKindTransfer,
    Implied,
}

/// <summary>Money (or shares, valued) crossing the account boundary on <see cref="Date"/> (start of day).</summary>
public sealed record ExternalFlow(DateOnly Date, decimal Amount, FlowKind Kind, Guid TransactionId, bool IsValued);

public enum OpeningClass
{
    /// <summary>Nothing was held before the first transaction — the history is complete.</summary>
    None,

    /// <summary>A position was already held before the imported history starts.</summary>
    PreHistory,

    /// <summary>Rolling the broker's position back gives a negative quantity: the ledger and broker disagree.</summary>
    Inconsistent,
}

/// <summary>
/// A holding's (or cash's, with a null id) position the day before the account's first transaction.
/// <see cref="Verified"/> is false when no broker statement or opening balance exists to check it against — the
/// position is then <i>assumed</i> to start at zero.
/// </summary>
public sealed record DerivedOpening(
    Guid? HoldingId, string? Symbol, DateOnly AsOf, decimal Quantity, OpeningClass Class, bool Verified = true);

public enum FindingCode
{
    QuantityMismatch,
    CashMismatch,
    NegativePosition,
    UnmatchedSplit,
    PreHistoryPosition,
}

/// <summary>A reconciliation observation the engine makes while building (surfaced by Data Health later).</summary>
public sealed record ReconciliationFinding(
    FindingCode Code,
    Guid? HoldingId,
    string? Symbol,
    DateOnly Date,
    decimal? LedgerQuantity,
    decimal? BrokerQuantity,
    decimal? DifferenceValue,
    bool IsMaterial);
