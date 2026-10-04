import { PortfolioImportResult, PortfolioImportStatus } from '../../core/api/models/imports.models';
import { accountResult, candidate, importResult, selection } from './testing/import-fixtures';
import {
  accountMismatchMessage,
  candidateEvidence,
  dateRangeText,
  routingNote,
  selectionQuestion,
  alreadyImportedNote,
  formatImportedAt,
  undoPreviewLines,
} from './import-text';

const RESULT: PortfolioImportResult = importResult({ accounts: [] });

describe('import text', () => {
  it('formats an import timestamp and passes unparseable values through', () => {
    expect(formatImportedAt('2026-10-03T16:05:00Z')).toContain('2026');
    expect(formatImportedAt('not a date')).toBe('not a date');
    expect(formatImportedAt(null)).toBe('');
  });

  it('explains a re-upload of an already imported file, and nothing else', () => {
    expect(alreadyImportedNote(RESULT)).toBeNull();
    const note = alreadyImportedNote({ ...RESULT, status: PortfolioImportStatus.AlreadyImported });
    expect(note).toContain('already imported on');
    expect(note).toContain('undo that import first');
  });

  it('names the accounts a mismatched file is for', () => {
    expect(accountMismatchMessage(['…1111', '…2222'])).toContain('different accounts (…1111, …2222)');
    expect(accountMismatchMessage([])).toContain('a different account.');
    expect(accountMismatchMessage(undefined)).toContain('a different account.');
  });

  it('lists only the parts of an undo that apply', () => {
    const minimal = undoPreviewLines({
      importBatchId: 'b1', fileName: 'a.qfx', transactions: 1, snapshots: 0, updatesReverted: 0,
      holdingsRemoved: 0, accountsRemoved: 0, laterImportsReplayed: [], laterImportsWithoutFile: [],
    });
    expect(minimal).toEqual(['Removes 1 transaction and 0 statement positions it added.']);

    const full = undoPreviewLines({
      importBatchId: 'b1', fileName: 'a.qfx', transactions: 3, snapshots: 2, updatesReverted: 4,
      holdingsRemoved: 1, accountsRemoved: 1, laterImportsReplayed: ['later.xlsx'], laterImportsWithoutFile: ['old.qfx'],
    });
    expect(full.length).toBe(5);
    expect(full[1]).toBe('Restores 4 earlier rows it changed.');
    expect(full[3]).toContain('later.xlsx');
    expect(full[4]).toContain('old.qfx');
  });

  it('explains how a file found its account', () => {
    expect(routingNote(accountResult({ routing: { method: 'Fingerprint', matchingRows: 2514 } }))).toBe(
      'Matched by 2,514 transactions already in this account.',
    );
    expect(routingNote(accountResult({ routing: { method: 'AccountNumber', matchingRows: null } }))).toBe(
      'Matched by account number.',
    );
    expect(routingNote(accountResult({ routing: { method: 'Created', matchingRows: null } }))).toContain('new account');
    expect(routingNote(accountResult({ routing: { method: 'Uploaded', matchingRows: null } }))).toBeNull();
  });

  it('formats the dates a file covers', () => {
    expect(dateRangeText('2025-01-02', '2025-12-31')).toBe('Jan 2, 2025 – Dec 31, 2025');
    expect(dateRangeText('2025-01-02', '2025-01-02')).toBe('Jan 2, 2025');
    expect(dateRangeText(null, null)).toBeNull();
  });

  it('states the evidence for a candidate account', () => {
    expect(candidateEvidence(candidate({ matchingRows: 2514, sharedTickers: 3 }))).toBe(
      '2,514 matching transactions · 3 shared funds',
    );
    expect(candidateEvidence(candidate({ matchingRows: 1 }))).toBe('1 matching transaction');
  });

  it('asks the right question for each reason', () => {
    expect(selectionQuestion(selection())).toContain("doesn't match any account yet (120 transactions, Jan 2, 2025 – Dec 31, 2025)");
    expect(selectionQuestion(selection({ candidates: [candidate()] }))).toBe(
      'Which account is this file for? (120 transactions, Jan 2, 2025 – Dec 31, 2025)',
    );
    expect(selectionQuestion(selection({ reason: 'UnknownAccountNumber', fileAccountNumber: 'AAA111' }))).toContain(
      "account …A111",
    );
  });
});
