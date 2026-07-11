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
  AccountNotFound = 2,
  PortfolioNotFound = 3,
  FileHasNoAccountInfo = 4,
  FileHasAccountInfo = 5,
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
}
