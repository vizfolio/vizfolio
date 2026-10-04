import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  PortfolioImportResult,
  PortfolioImportStatus,
  StatementAssignment,
} from '../../../core/api/models/imports.models';
import { candidate, importResult, selection } from '../testing/import-fixtures';
import { ImportDropZone } from './import-drop-zone';

class MockApi {
  /** Responses per file name, consumed in order. */
  responses: Record<string, Observable<PortfolioImportResult>[]> = {};
  calls: { file: string; sourceSystem?: string; assignments?: StatementAssignment[] }[] = [];
  getImportParsers() {
    return of([]);
  }
  getImports() {
    return of({ imports: [], transactionsImportedBeforeHistory: 0 });
  }
  getAccountPerformance() {
    return throwError(() => new Error('not needed here'));
  }
  importPortfolioFile(_pid: string, file: File, sourceSystem?: string, assignments?: StatementAssignment[]) {
    this.calls.push({ file: file.name, sourceSystem, assignments });
    return this.responses[file.name]?.shift() ?? of(importResult());
  }
}

function setup(api: MockApi) {
  TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }] });
  const fixture = TestBed.createComponent(ImportDropZone);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.detectChanges();
  let changes = 0;
  fixture.componentInstance.changed.subscribe(() => changes++);
  return { fixture, cmp: fixture.componentInstance as any, el: fixture.nativeElement as HTMLElement, changes: () => changes };
}

const asking = importResult({
  status: PortfolioImportStatus.NeedsAccountSelection,
  accounts: [],
  selections: [selection({ candidates: [candidate({ accountId: 'a2', name: 'Taxable', matchingRows: 3 })] })],
});

describe('ImportDropZone', () => {
  it('imports several files one after another and shows each summary', () => {
    const api = new MockApi();
    const { fixture, cmp, el, changes } = setup(api);

    cmp.onFiles([new File(['1'], 'all.qfx'), new File(['2'], 'ira.xlsx')]);
    fixture.detectChanges();

    expect(api.calls.map((c) => c.file)).toEqual(['all.qfx', 'ira.xlsx']);
    expect(el.querySelectorAll('app-import-summary').length).toBe(2);
    expect(changes()).toBe(2);
  });

  it('pauses on a question, sends the answer with the file again, then carries on', () => {
    const api = new MockApi();
    api.responses['taxable.xlsx'] = [of(asking)];
    const { fixture, cmp, el } = setup(api);

    cmp.onFiles([new File(['1'], 'taxable.xlsx'), new File(['2'], 'later.qfx')]);
    fixture.detectChanges();

    expect(api.calls.map((c) => c.file)).toEqual(['taxable.xlsx']); // later.qfx waits for the answer
    expect(el.querySelector('app-account-picker')).not.toBeNull();

    const item = cmp.items().find((i: { file: File }) => i.file.name === 'taxable.xlsx');
    cmp.onChosen(item, { fileAccountNumber: '', accountId: 'a2' });
    fixture.detectChanges();

    expect(api.calls.map((c) => c.file)).toEqual(['taxable.xlsx', 'taxable.xlsx', 'later.qfx']);
    expect(api.calls[1].assignments).toEqual([{ fileAccountNumber: '', accountId: 'a2' }]);
    expect(el.querySelector('app-account-picker')).toBeNull();
  });

  it('moves on when a file is skipped', () => {
    const api = new MockApi();
    api.responses['mystery.xlsx'] = [of(asking)];
    const { fixture, cmp, el } = setup(api);

    cmp.onFiles([new File(['1'], 'mystery.xlsx'), new File(['2'], 'next.qfx')]);
    cmp.onSkipped(cmp.items().find((i: { file: File }) => i.file.name === 'mystery.xlsx'));
    fixture.detectChanges();

    expect(api.calls.map((c) => c.file)).toEqual(['mystery.xlsx', 'next.qfx']);
    expect(el.textContent).toContain('mystery.xlsx — skipped.');
  });

  it('reports a failed file and carries on with the rest', () => {
    const api = new MockApi();
    api.responses['notes.txt'] = [throwError(() => new HttpErrorResponse({ status: 415 }))];
    const { fixture, cmp, el } = setup(api);

    cmp.onFiles([new File(['1'], 'notes.txt'), new File(['2'], 'all.qfx')]);
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('notes.txt — Unsupported file type');
    expect(api.calls.map((c) => c.file)).toEqual(['notes.txt', 'all.qfx']);
  });

  it('forwards the format chosen under Advanced', () => {
    const api = new MockApi();
    const { cmp } = setup(api);

    cmp.sourceSystem.set('VANGUARD');
    cmp.onFiles([new File(['1'], 'report.xlsx')]);

    expect(api.calls[0].sourceSystem).toBe('VANGUARD');
  });
});
