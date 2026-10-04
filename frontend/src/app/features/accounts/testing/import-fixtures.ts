import {
  AccountImportResult,
  AccountSelection,
  PortfolioImportResult,
  PortfolioImportStatus,
  RoutingCandidate,
} from '../../../core/api/models/imports.models';

/** An account's result within an import, with test-friendly defaults. */
export function accountResult(overrides: Partial<AccountImportResult> = {}): AccountImportResult {
  return {
    accountId: 'a1',
    created: false,
    institutionCode: 'vanguard.com',
    accountNumber: '1234',
    considered: 4,
    inserted: 3,
    skipped: 1,
    failed: 0,
    failures: [],
    impliedContributions: 0,
    impliedContributionsAmount: 0,
    updated: 0,
    snapshotsInserted: 0,
    routing: null,
    firstDate: null,
    lastDate: null,
    ...overrides,
  };
}

/** An import response, with test-friendly defaults (a successful single-account QFX import). */
export function importResult(overrides: Partial<PortfolioImportResult> = {}): PortfolioImportResult {
  return {
    status: PortfolioImportStatus.Success,
    sourceSystem: 'QFX',
    accounts: [accountResult()],
    duration: 'PT0.1S',
    importBatchId: 'b1',
    importedAt: '2026-10-03T16:05:00Z',
    warnings: [],
    fileAccountNumbers: [],
    parserDisplayName: 'OFX / QFX statement',
    selections: [],
    error: null,
    ...overrides,
  };
}

export function candidate(overrides: Partial<RoutingCandidate> = {}): RoutingCandidate {
  return {
    accountId: 'a1',
    name: 'IRA',
    institutionCode: 'vanguard.com',
    accountNumberMasked: '…1111',
    matchingRows: 0,
    rowsInAccountRange: 0,
    sharedTickers: 0,
    ...overrides,
  };
}

/** A statement the import asks about (by default: a file without an account number). */
export function selection(overrides: Partial<AccountSelection> = {}): AccountSelection {
  return {
    fileAccountNumber: '',
    institutionCode: 'vanguard.com',
    reason: 'NoAccountNumber',
    rows: 120,
    firstDate: '2025-01-02',
    lastDate: '2025-12-31',
    candidates: [],
    suggestedAccountId: null,
    ...overrides,
  };
}
