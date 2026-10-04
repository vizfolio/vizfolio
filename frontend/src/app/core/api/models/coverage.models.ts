/**
 * TypeScript mirrors of the account history-coverage and opening-balance DTOs.
 *
 * Source of truth:
 *   backend/src/Vizfolio.Api/Endpoints/Portfolios/HistoryCoverageResponses.cs
 *
 * Dates are serialized as "YYYY-MM-DD" (DateOnly); kept as strings, parse at the edge.
 */

/** GET /api/portfolios/{portfolioId}/accounts/{accountId}/history-coverage */
export interface HistoryCoverageResponse {
  accountId: string;
  firstTransactionDate: string | null;
  earliestSnapshotDate: string | null;
  /** true iff the ledger starts before the earliest snapshot (a fillable gap). */
  hasHistoryGap: boolean;
  suggestedOpeningDate: string | null;
  openingBalanceSnapshotCount: number;
  statementSnapshotCount: number;
  brokerPositionSnapshotCount: number;
}

/** A single holding line in an opening-balance submission. */
export interface OpeningBalanceHoldingInput {
  symbol: string;
  units: number;
  marketValue?: number | null;
  unitPrice?: number | null;
  costBasis?: number | null;
  currencyCode?: string | null;
  cusip?: string | null;
}

/**
 * POST /api/portfolios/{portfolioId}/accounts/{accountId}/opening-balance
 * `portfolioId`/`accountId` are in the route; included in the body to round-trip the DTO.
 */
export interface SetOpeningBalanceRequest {
  portfolioId: string;
  accountId: string;
  asOf: string;
  defaultCurrencyCode?: string | null;
  holdings: OpeningBalanceHoldingInput[];
}

/** Per-holding outcome of an opening-balance submission. */
export interface OpeningBalanceHoldingOutcome {
  symbol: string;
  accountHoldingId: string;
  quantity: number;
  marketValue: number | null;
  unitPrice: number | null;
  costBasis: number | null;
  currencyCode: string | null;
  created: boolean;
}

export interface OpeningBalanceResponse {
  accountId: string;
  asOf: string;
  snapshotsCreated: number;
  snapshotsUpdated: number;
  holdings: OpeningBalanceHoldingOutcome[];
}

/** How a starting position was worked out (see OpeningPositionsResponse). */
export type OpeningPositionClass = 'None' | 'PreHistory' | 'Inconsistent';

/** One holding's (or, with a null id, the cash's) position the day before the first transaction. */
export interface OpeningPosition {
  accountHoldingId: string | null;
  symbol: string | null;
  quantity: number;
  /** None = nothing held; PreHistory = held before the imported history; Inconsistent = can't be derived. */
  class: OpeningPositionClass | string;
  /** False when no statement exists to derive it from (the position is assumed to start at zero). */
  verified: boolean;
  unitPrice: number | null;
  marketValue: number | null;
}

/** GET /api/portfolios/{portfolioId}/accounts/{accountId}/opening-positions */
export interface OpeningPositionsResponse {
  /** The day before the account's first transaction; null when it has none. */
  asOf: string | null;
  holdings: OpeningPosition[];
  cash: OpeningPosition;
}
