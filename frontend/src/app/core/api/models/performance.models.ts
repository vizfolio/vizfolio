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
export interface PortfolioPerformance {
  from: string;
  to: string;
  startingBalance: PerformanceBalance;
  endingBalance: PerformanceBalance;
  contributions: PerformanceContributions;
  returns: PerformanceReturns;
  currencyCode: string;
}
