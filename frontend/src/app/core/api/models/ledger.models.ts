/**
 * TypeScript mirror of the backend ledger DTO.
 *
 * Source of truth: backend/src/Vizfolio.Api/Endpoints/Portfolios/LedgerResponses.cs
 *
 * A `type` (not `interface`) so it satisfies the `DataTable` row constraint
 * (`Record<string, unknown>`). `type` is the normalized transaction type; `sourceType`
 * preserves the broker's original label when richer.
 */
export type LedgerEntry = {
  accountTransactionId: string;
  tradeDate: string;
  settlementDate: string | null;
  type: string;
  sourceType: string | null;
  ticker: string | null;
  cusip: string | null;
  accountHoldingId: string | null;
  holdingName: string | null;
  quantity: number | null;
  price: number | null;
  amount: number;
  fees: number | null;
  currencyCode: string | null;
  memo: string | null;
};
