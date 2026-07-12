/**
 * TypeScript mirror of the backend holdings DTO.
 *
 * Source of truth: backend/src/Vizfolio.Api/Endpoints/Portfolios/HoldingResponses.cs
 *
 * A `type` (not `interface`) so it satisfies the `DataTable` row constraint
 * (`Record<string, unknown>`). Valuation fields are null when the holding has no snapshot
 * on or before the requested `asOf` date (`hasSnapshot === false`).
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
};
