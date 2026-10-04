import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { ImportHistory, ImportUndoSummary } from '../../../core/api/models/imports.models';
import { ImportHistoryList } from './import-history-list';

const HISTORY: ImportHistory = {
  imports: [
    {
      importBatchId: 'b2', fileName: 'report.xlsx', sourceSystem: 'VANGUARD', importedAt: '2026-10-02T10:00:00Z',
      reprocessedAt: null, status: 'Active', undoneAt: null, hasStoredFile: true,
      accounts: [{ accountId: 'a2', accountName: 'IRA', created: false, inserted: 5, skipped: 0, updated: 2, snapshotsInserted: 0, failed: 0 }],
      warnings: [{ code: 'UnmappedLabel', message: 'X was imported as Other.', count: 1, samples: ['row 7'] }],
    },
    {
      importBatchId: 'b1', fileName: 'all.qfx', sourceSystem: 'QFX', importedAt: '2026-10-01T10:00:00Z',
      reprocessedAt: null, status: 'Undone', undoneAt: '2026-10-01T11:00:00Z', hasStoredFile: false,
      accounts: [{ accountId: 'a1', accountName: 'Brokerage', created: true, inserted: 3, skipped: 0, updated: 0, snapshotsInserted: 2, failed: 0 }],
      warnings: [],
    },
  ],
  transactionsImportedBeforeHistory: 40,
};

const PREVIEW: ImportUndoSummary = {
  importBatchId: 'b2', fileName: 'report.xlsx', transactions: 5, snapshots: 0, updatesReverted: 2,
  holdingsRemoved: 0, accountsRemoved: 0, laterImportsReplayed: [], laterImportsWithoutFile: [],
};

class MockApi {
  importFileUrl(portfolioId: string, batchId: string) {
    return `/api/portfolios/${portfolioId}/imports/${batchId}/file`;
  }
  loads = 0;
  undone: string | null = null;
  undoResult: Observable<ImportUndoSummary> = of(PREVIEW);
  getImports() {
    this.loads++;
    return of(HISTORY);
  }
  getImportUndoPreview() {
    return of(PREVIEW);
  }
  undoImport(_pid: string, batchId: string) {
    this.undone = batchId;
    return this.undoResult;
  }
}

function setup(api: MockApi, accountId: string | null = null) {
  TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }] });
  const fixture = TestBed.createComponent(ImportHistoryList);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('accountId', accountId);
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any, el: fixture.nativeElement as HTMLElement };
}

describe('ImportHistoryList', () => {
  it('lists imports newest first with what each did, and marks undone ones without an Undo button', () => {
    const { el } = setup(new MockApi());

    const items = el.querySelectorAll('li.import');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('report.xlsx');
    expect(items[0].textContent).toContain('IRA: 5 added, 2 updated');
    expect(items[0].querySelector('button.undo')).not.toBeNull();
    expect(items[1].classList).toContain('import--undone');
    expect(items[1].querySelector('button.undo')).toBeNull();
    expect(el.textContent).toContain("40 transactions were imported before import history was kept");
  });

  it('shows only imports into the account on an account page', () => {
    const { el } = setup(new MockApi(), 'a1');

    const items = el.querySelectorAll('li.import');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('all.qfx');
  });

  it('asks before undoing, showing what will change, then undoes, reloads and notifies the parent', () => {
    const api = new MockApi();
    const { fixture, cmp, el } = setup(api);
    let emitted: ImportUndoSummary | null = null;
    cmp.undone.subscribe((s: ImportUndoSummary) => (emitted = s));

    cmp.startUndo(HISTORY.imports[0]);
    fixture.detectChanges();
    expect(el.querySelector('.confirm')?.textContent).toContain('Undo the import of report.xlsx?');
    expect(el.querySelector('.preview')?.textContent).toContain('Removes 5 transactions');
    expect(api.undone).toBeNull();

    const loadsBefore = api.loads;
    cmp.confirmUndo();
    fixture.detectChanges();
    expect(api.undone).toBe('b2');
    expect(emitted).toEqual(PREVIEW);
    expect(api.loads).toBe(loadsBefore + 1);
    expect(el.querySelector('.confirm')).toBeNull();
    expect(el.querySelector('.notice')?.textContent).toContain('Undid the import of report.xlsx');
  });

  it('cancelling leaves the import in place', () => {
    const api = new MockApi();
    const { fixture, cmp, el } = setup(api);

    cmp.startUndo(HISTORY.imports[0]);
    cmp.cancelUndo();
    fixture.detectChanges();

    expect(el.querySelector('.confirm')).toBeNull();
    expect(api.undone).toBeNull();
  });

  it('explains an import that was already undone elsewhere', () => {
    const api = new MockApi();
    api.undoResult = throwError(() => new HttpErrorResponse({ status: 409 }));
    const { fixture, cmp, el } = setup(api);

    cmp.startUndo(HISTORY.imports[0]);
    cmp.confirmUndo();
    fixture.detectChanges();

    expect(el.querySelector('.confirm [role="alert"]')?.textContent).toContain('already been undone');
  });

  it('links each stored file for download under its own name, and none for imports whose file was not kept', () => {
    const { el } = setup(new MockApi());

    const items = el.querySelectorAll('li.import');
    const link = items[0].querySelector('a.download') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/api/portfolios/p1/imports/b2/file');
    expect(link.hasAttribute('download')).toBe(true);
    expect(link.getAttribute('aria-label')).toBe('Download report.xlsx');
    expect(items[1].querySelector('a.download')).toBeNull();
  });
});
