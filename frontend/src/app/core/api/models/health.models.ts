/**
 * GET /api/portfolios/{id}/health and …/accounts/{accountId}/health. Source of truth:
 *   backend/src/Vizfolio.Application/Portfolios/Health/DataHealthModels.cs
 */
export type HealthStatus = 'Healthy' | 'Info' | 'NeedsAttention';

/** Blocking: makes a headline number blank or approximate. Info: worth knowing. */
export type HealthSeverity = 'Blocking' | 'Info';

export type HealthCode =
  | 'QuantityMismatch'
  | 'CashMismatch'
  | 'NegativePosition'
  | 'UnmatchedSplit'
  | 'PreHistoryPosition'
  | 'UnpricedHolding'
  | 'StalePrice'
  | 'UnvaluedTransfer'
  | 'ImportWarning'
  | 'ImpliedContributions';

export type HealthActionKind =
  | 'None'
  | 'FetchPrices'
  | 'AddPriceProviderKey'
  | 'AdjustStartingPosition'
  | 'ReimportFile'
  | 'ReviewImpliedContributions';

export interface HealthAction {
  kind: HealthActionKind;
  label: string | null;
}

export interface HealthDetails {
  ledgerQuantity: number | null;
  brokerQuantity: number | null;
  amount: number | null;
  count: number | null;
  priceFetchOutcome: string | null;
  priceFetchMessage: string | null;
  pricesPending: boolean;
  warningCode: string | null;
  samples: string[];
  files: string[];
}

export interface HealthFinding {
  code: HealthCode;
  severity: HealthSeverity;
  /** Null for a finding about a file that went into several accounts. */
  accountId: string | null;
  holdingId: string | null;
  symbol: string | null;
  from: string | null;
  to: string | null;
  message: string;
  action: HealthAction;
  details: HealthDetails;
}

export interface AccountHealth {
  accountId: string;
  name: string;
  status: HealthStatus;
  blocking: number;
  info: number;
}

export interface DataHealthReport {
  status: HealthStatus;
  /** The reporting currency amounts in messages and details are in. */
  currencyCode: string;
  accounts: AccountHealth[];
  findings: HealthFinding[];
}

/** GET …/accounts/{accountId}/implied-contributions — the dry run behind the stored implied contributions. */
export interface ImpliedContributionPreview {
  tolerance: number;
  totalAmount: number;
  endingCash: number;
  byYear: { year: number; amount: number; count: number }[];
}
