/**
 * TypeScript mirrors of the backend performance/portfolio DTOs.
 *
 * Source of truth:
 *   backend/src/Vizfolio.Api/Endpoints/Portfolios/PerformanceResponses.cs
 *   backend/src/Vizfolio.Api/Endpoints/Portfolios/*Endpoint.cs
 *
 * Dates are serialized by the API as ISO strings (DateOnly -> "YYYY-MM-DD",
 * DateTimeOffset -> full ISO-8601). Kept as `string` here; parse at the edge.
 */

/** GET /api/portfolios and GET /api/portfolios/{id} */
export interface PortfolioSummary {
  portfolioId: string;
  name: string;
  createdAt: string;
  accountCount: number;
}

/** GET /api/portfolios/{id}/accounts and .../accounts/{accountId} */
export interface AccountSummary {
  accountId: string;
  portfolioId: string;
  name: string;
  institutionCode: string;
  accountNumber: string;
  accountType: string | null;
  createdAt: string;
  transactionCount: number;
}

/** A portfolio/account value at a point in time, with completeness metadata. */
export interface PerformanceBalance {
  value: number;
  isComplete: boolean;
  snapshotAsOf: string | null;
  holdingsCovered: number;
  holdingsMissingSnapshot: number;
  /** Up to 10 holdings that couldn't be valued (a null symbol is the account's cash) and why. */
  missing?: PerformanceMissing[];
}

export type PerformanceMissingCause =
  | 'NoPrice'
  | 'StalePrice'
  | 'NegativePosition'
  | 'MaterialMismatch'
  | 'BeforeHistory'
  /** No (recent) price yet, but a background price fetch for the account is queued or running. */
  | 'PricesPending';

export interface PerformanceMissing {
  accountId: string;
  accountHoldingId: string | null;
  symbol: string | null;
  cause: PerformanceMissingCause | string;
}

/** Cash flows in/out over the period. Sign convention: deposits +, withdrawals -. */
export interface PerformanceContributions {
  net: number;
  deposits: number;
  withdrawals: number;
  count: number;
}

/**
 * A single return figure; `rate` is null when it cannot be computed (`reason` says why), and `basis`
 * says whether it is per year ('Annualized') or the total ('Period'). Both forms are also given
 * explicitly: `periodRate` is the total over the period and `annualizedRate` the per-year rate that
 * compounds to it (periods of a year or more only).
 * `fallbackReason` is set when the preferred method couldn't be used and `method` names the
 * approximation used instead (e.g. 'ModifiedDietz' because a day couldn't be valued — the cause).
 */
export interface PerformanceReturn {
  rate: number | null;
  /** 'XIRR' (money-weighted), 'DailyValuedTWR' (time-weighted) or 'ModifiedDietz' (approximation). */
  method: string;
  basis: 'Period' | 'Annualized' | string;
  reason: string | null;
  annualizedRate: number | null;
  periodRate: number | null;
  fallbackReason: string | null;
}

/**
 * `moneyWeighted` is the headline "Your return" (XIRR — your personal rate of return);
 * `timeWeighted` is the "Investment return" (how the investments did, regardless of when money was
 * added).
 */
export interface PerformanceReturns {
  timeWeighted: PerformanceReturn;
  moneyWeighted: PerformanceReturn;
}

/** GET /api/portfolios/{id}/performance (and account-scoped variant). */
/** Spacing of the value-over-period series, chosen by the API from the period's length. */
export type PerformanceSeriesInterval = 'Weekly' | 'Monthly' | 'Quarterly';

/**
 * One chart point: the balance at the close of `date` (null when a holding couldn't be valued —
 * drawn as a gap), the deposits / withdrawals (negative) since the previous point, the cumulative
 * time-weighted return from the period's start (decimal rate; the last point equals
 * `returns.timeWeighted`, the investment return — not the money-weighted headline) and the
 * cumulative investment gain (value − starting balance − net contributions to date).
 * Return and gain are null where they can't be computed.
 */
export interface PerformanceSeriesPoint {
  date: string;
  value: number | null;
  deposits: number;
  withdrawals: number;
  cumulativeReturn: number | null;
  investmentGain: number | null;
}

/**
 * Value / returns over the period. The first point is the opening (starting balance, the day before `from`);
 * the last is `to` (ending balance).
 */
export interface PerformanceSeries {
  interval: PerformanceSeriesInterval;
  points: PerformanceSeriesPoint[];
}

export interface PortfolioPerformance {
  from: string;
  to: string;
  startingBalance: PerformanceBalance;
  endingBalance: PerformanceBalance;
  contributions: PerformanceContributions;
  returns: PerformanceReturns;
  currencyCode: string;
  series: PerformanceSeries;
}
