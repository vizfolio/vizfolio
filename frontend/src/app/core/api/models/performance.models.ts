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
}

/** Cash flows in/out over the period. Sign convention: deposits +, withdrawals -. */
export interface PerformanceContributions {
  net: number;
  deposits: number;
  withdrawals: number;
  count: number;
}

/** A single return figure; `rate` is null when it cannot be computed. */
export interface PerformanceReturn {
  rate: number | null;
  method: string;
  basis: 'Period' | 'Annualized' | string;
  reason: string | null;
}

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
 * time-weighted return from the period's start (decimal rate; the last point equals the headline
 * TWR) and the cumulative investment gain (value − starting balance − net contributions to date).
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
