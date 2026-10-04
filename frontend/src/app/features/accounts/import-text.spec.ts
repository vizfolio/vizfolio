import { PortfolioImportResult, PortfolioImportStatus } from '../../core/api/models/imports.models';
import { accountMismatchMessage, alreadyImportedNote, formatImportedAt, undoPreviewLines } from './import-text';

const RESULT: PortfolioImportResult = {
  status: PortfolioImportStatus.Success,
  sourceSystem: 'QFX',
  accounts: [],
  duration: 'PT0S',
  importBatchId: 'b1',
  importedAt: '2026-10-03T16:05:00Z',
  warnings: [],
  fileAccountNumbers: [],
};

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
});
