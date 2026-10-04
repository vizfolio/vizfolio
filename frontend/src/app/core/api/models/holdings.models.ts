/**
 * TypeScript mirror of the backend holdings DTO.
 *
 * Source of truth: backend/src/Vizfolio.Api/Endpoints/Portfolios/HoldingResponses.cs
 *
 * A `type` (not `interface`) so it satisfies the `DataTable` row constraint
 * (`Record<string, unknown>`). Positions are valued by the same rules as performance: ledger
 * quantity × a recent price, $1.00 a share for a money-market fund, or the broker's snapshot.
 * `status` says whether a position was valued, not held ($0), or couldn't be valued (`marketValue`
 * null). `hasSnapshot` / `snapshotAsOf` / `source` describe the latest snapshot on or before `asOf`.
 */
export type HoldingRow = {
  accountHoldingId: string;
  kind: string;
  symbol: string | null;
  name: string | null;
  cusip: string | null;
  isin: string | null;
  currencyCode: string | null;
  hasSnapshot: boolean;
  snapshotAsOf: string | null;
  source: string | null;
  quantity: number | null;
  unitPrice: number | null;
  marketValue: number | null;
  costBasis: number | null;
  gainLoss: number | null;
  status: HoldingValuationStatus;
  /** Where a value came from; null when the position wasn't valued. */
  valuationSource: 'Price' | 'Snapshot' | 'StableNav' | null;
  /** Date of the price or snapshot the value is based on. */
  priceAsOf: string | null;
  /**
   * Why a `Missing` row couldn't be valued (`NoPrice`, `StalePrice`, `PricesPending` while prices are still
   * downloading, …); null otherwise.
   */
  missingCause?: string | null;
};

export type HoldingValuationStatus = 'Valued' | 'NotHeld' | 'Missing';
