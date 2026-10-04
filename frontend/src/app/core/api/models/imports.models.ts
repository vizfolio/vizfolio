/**
 * TypeScript mirrors of the portfolio-import DTOs.
 *
 * Source of truth:
 *   backend/src/Vizfolio.Application/PortfolioImports/Models/PortfolioImportResult.cs
 *   backend/src/Vizfolio.Application/PortfolioImports/Models/AccountImportResult.cs
 *
 * NOTE on the status wire format: the backend (FastEndpoints 8 / System.Text.Json Web
 * defaults) serializes the `PortfolioImportStatus` enum as its numeric value. This numeric
 * TS enum is declared in the same order as the C# enum so wire values map directly. If a
 * future backend change adds a JsonStringEnumConverter, revisit this.
 */

/** Outcome of an import request. Values must match the C# enum declaration order. */
export enum PortfolioImportStatus {
  Success = 0,
  UnsupportedFormat = 1,
  UnknownParser = 2,
  AccountNotFound = 3,
  PortfolioNotFound = 4,
  FileHasNoAccountInfo = 5,
  FileHasAccountInfo = 6,
  /** This exact file was already imported (and not undone); nothing was written. */
  AlreadyImported = 7,
  /** The file names its accounts, and none is the account it was uploaded to (HTTP 422). */
  AccountMismatch = 8,
  /**
   * Some statements couldn't be placed on their own; nothing was written. `selections` lists them with candidate
   * accounts — send the file again with `assignments`.
   */
  NeedsAccountSelection = 9,
  /** Uploaded to one account but the file clearly belongs to another; nothing was written. */
  LikelyOtherAccount = 10,
  /** The assignments sent with the file don't work (HTTP 400; see `error`). */
  InvalidAccountSelection = 11,
}

/** How an account was chosen for a file's rows (AccountImportResult.routing.method). */
export type RoutingMethod = 'Uploaded' | 'AccountNumber' | 'Fingerprint' | 'UserSelected' | 'Created';

export interface AccountRouting {
  method: RoutingMethod;
  /** For Fingerprint: how many of the file's transactions the account already held. */
  matchingRows: number | null;
}

/**
 * How strongly a file's rows point at an account: how many it already holds out of the file's rows dated within
 * its history, and how many tickers they share. Source of truth:
 *   backend/src/Vizfolio.Application/PortfolioImports/Models/ImportRouting.cs
 */
export interface RoutingCandidate {
  accountId: string;
  name: string;
  institutionCode: string;
  accountNumberMasked: string;
  matchingRows: number;
  rowsInAccountRange: number;
  sharedTickers: number;
}

export type AccountSelectionReason = 'NoAccountNumber' | 'UnknownAccountNumber' | 'LikelyOtherAccount';

/** A statement the import couldn't place on its own. */
export interface AccountSelection {
  /** The file's account number (normalized), or '' when it has none — echo it in the assignment. */
  fileAccountNumber: string;
  institutionCode: string | null;
  reason: AccountSelectionReason;
  rows: number;
  firstDate: string | null;
  lastDate: string | null;
  candidates: RoutingCandidate[];
  /** The account the evidence points to — pre-select it. */
  suggestedAccountId: string | null;
}

/** Where one statement goes when the file is sent again: an existing account, or a new one. */
export interface StatementAssignment {
  fileAccountNumber: string;
  accountId?: string;
  newAccount?: { name?: string; institutionCode?: string; accountNumber?: string };
}

/**
 * A registered import parser advertised by GET /api/imports/parsers, used to populate the
 * "Format" override dropdown. Source of truth:
 *   backend/src/Vizfolio.Api/Endpoints/Portfolios/ListImportParsersEndpoint.cs
 */
export interface ImportParser {
  sourceSystem: string;
  displayName: string;
  fileExtensions: string[];
}

/**
 * Something in the file that wasn't fully understood, grouped by code and message.
 * Source of truth: backend/src/Vizfolio.Application/PortfolioImports/Models/ImportWarning.cs
 * (codes in ImportWarningCodes, e.g. UnmappedLabel, UnknownAggregate, RowFailed).
 */
export interface ImportWarning {
  code: string;
  message: string;
  count: number;
  samples: string[];
}

/** A single row/account that could not be processed. */
export interface PortfolioImportFailure {
  key: string;
  reason: string;
}

/** Per-account result within a portfolio import. */
export interface AccountImportResult {
  accountId: string;
  created: boolean;
  institutionCode: string | null;
  accountNumber: string | null;
  considered: number;
  inserted: number;
  skipped: number;
  failed: number;
  failures: PortfolioImportFailure[];
  /** Stored rows the parser now maps differently, updated in place (also counted in `skipped`). */
  updated: number;
  /** Statement positions and cash balances recorded. */
  snapshotsInserted: number;
  /**
   * Contributions the account's history implies but never recorded (purchases with no deposit),
   * stored as "Implied contribution" ledger rows. Totals for the whole account after this import.
   */
  impliedContributions: number;
  impliedContributionsAmount: number;
  /** How this account was chosen for the file. */
  routing: AccountRouting | null;
  /** Earliest and latest trade dates of the file's rows for this account. */
  firstDate: string | null;
  lastDate: string | null;
}

/**
 * Result of POST .../imports (portfolio-scoped) and .../accounts/{id}/imports (account-scoped).
 * `duration` is serialized as an ISO-8601 duration string (TimeSpan).
 */
export interface PortfolioImportResult {
  status: PortfolioImportStatus;
  sourceSystem: string | null;
  accounts: AccountImportResult[];
  duration: string;
  /** The import's record (see the Imports history); for AlreadyImported, the earlier import. */
  importBatchId: string | null;
  /** ISO timestamp of the import — for AlreadyImported, of the earlier one. */
  importedAt: string | null;
  warnings: ImportWarning[];
  /** For AccountMismatch: the (masked, e.g. "…1234") account numbers the file contains. */
  fileAccountNumbers: string[];
  /** The detected format, for people (e.g. "Vanguard transaction report"). */
  parserDisplayName: string | null;
  /** For NeedsAccountSelection / LikelyOtherAccount: what to ask. */
  selections: AccountSelection[];
  /** For InvalidAccountSelection: what's wrong with the assignments. */
  error: string | null;
}

/**
 * GET /api/portfolios/{id}/imports. Source of truth:
 * backend/src/Vizfolio.Application/PortfolioImports/Services/ImportHistoryService.cs
 */
export interface ImportHistory {
  imports: ImportBatchItem[];
  /** Rows imported before imports were recorded: they belong to no import and can't be undone. */
  transactionsImportedBeforeHistory: number;
}

export interface ImportBatchItem {
  importBatchId: string;
  fileName: string;
  sourceSystem: string;
  importedAt: string;
  reprocessedAt: string | null;
  status: 'Active' | 'Undone';
  undoneAt: string | null;
  hasStoredFile: boolean;
  accounts: ImportBatchAccount[];
  warnings: ImportWarning[];
}

export interface ImportBatchAccount {
  accountId: string;
  accountName: string | null;
  created: boolean;
  inserted: number;
  skipped: number;
  updated: number;
  snapshotsInserted: number;
  failed: number;
}

/** GET .../imports/{id}/undo-preview and POST .../undo: what undo removes or reverts (or did). */
export interface ImportUndoSummary {
  importBatchId: string;
  fileName: string;
  transactions: number;
  snapshots: number;
  updatesReverted: number;
  holdingsRemoved: number;
  accountsRemoved: number;
  laterImportsReplayed: string[];
  laterImportsWithoutFile: string[];
}

/**
 * POST /api/imports/reprocess — stored import files re-read with the current parsers. Source of truth:
 * backend/src/Vizfolio.Application/PortfolioImports/Models/ImportBatchSummary.cs
 */
export interface ReprocessResult {
  /** Files re-read (those skipped aren't counted). */
  batches: number;
  inserted: number;
  updated: number;
  snapshotsInserted: number;
  perBatch: ReprocessedBatch[];
}

export interface ReprocessedBatch {
  importBatchId: string;
  fileName: string;
  inserted: number;
  updated: number;
  snapshotsInserted: number;
  /** Why the file wasn't re-read (e.g. its parser is gone); null when it was. */
  skipped: string | null;
}
