/**
 * TypeScript mirrors of the admin reference-data import DTOs.
 *
 * Source of truth:
 *   backend/src/Vizfolio.Application/Extracts/Models/ImportOptions.cs (ImportResult)
 *   backend/src/Vizfolio.Api/Endpoints/Admin/Imports/*Endpoint.cs
 *   backend/src/Vizfolio.Api/Endpoints/Admin/Portfolios/RelinkLedgerEndpoint.cs
 */

/** A single key/reason failure surfaced by an extracts import. */
export interface ImportFailure {
  key: string;
  reason: string;
}

/** A value the importer rewrote during ingestion, surfaced for upstream cleanup. */
export interface DataCleaningEntry {
  field: string;
  originalValue: string;
  reason: string;
  occurrences: number;
}

/**
 * Result of POST /api/admin/imports/securities and .../funds.
 * `duration` is an ISO-8601 duration string (TimeSpan).
 */
export interface ImportResult {
  considered: number;
  upserted: number;
  skipped: number;
  failed: number;
  failures: ImportFailure[];
  dataCleaning: DataCleaningEntry[];
  duration: string;
}

/** POST /api/admin/imports/securities */
export interface ImportSecuritiesRequest {
  tickers?: string[] | null;
  force?: boolean;
}

/** POST /api/admin/imports/funds */
export interface ImportFundsRequest {
  seriesIds?: string[] | null;
  force?: boolean;
}

/** POST /api/admin/imports/all */
export interface ImportAllRequest {
  force?: boolean;
}

/** Result of POST /api/admin/imports/all: both import phases plus the re-link count. */
export interface ImportAllResponse {
  securities: ImportResult;
  funds: ImportResult;
  relinked: number;
}

/** Result of POST /api/admin/portfolios/relink. */
export interface RelinkLedgerResponse {
  linked: number;
}
