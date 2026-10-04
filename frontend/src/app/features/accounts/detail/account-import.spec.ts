import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  ImportParser,
  PortfolioImportResult,
  PortfolioImportStatus,
} from '../../../core/api/models/imports.models';
import { candidate, importResult, selection } from '../testing/import-fixtures';
import { AccountImport } from './account-import';

const PARSERS: ImportParser[] = [
  { sourceSystem: 'QFX', displayName: 'OFX / QFX statement', fileExtensions: ['.qfx', '.ofx'] },
  { sourceSystem: 'VANGUARD', displayName: 'Vanguard transaction report', fileExtensions: ['.xlsx', '.xls'] },
];

const RESULT: PortfolioImportResult = importResult();

class MockApi {
  result: Observable<PortfolioImportResult> = of(RESULT);
  /** Answers after the first upload (e.g. once the user chose where the file goes). */
  nextResults: Observable<PortfolioImportResult>[] = [];
  file: File | null = null;
  sourceSystem: string | undefined;
  calls: { accountId: string; ignoreRoutingCheck: boolean }[] = [];
  getAccountPerformance() {
    return throwError(() => new Error('not needed here'));
  }
  getImports() {
    return of({ imports: [], transactionsImportedBeforeHistory: 0 });
  }
  getImportParsers() {
    return of(PARSERS);
  }
  importAccountFile(_pid: string, accountId: string, file: File, sourceSystem?: string, ignoreRoutingCheck = false) {
    this.file = file;
    this.sourceSystem = sourceSystem;
    this.calls.push({ accountId, ignoreRoutingCheck });
    return this.calls.length > 1 && this.nextResults.length > 0 ? this.nextResults.shift()! : this.result;
  }
}

function setup(api: MockApi) {
  TestBed.configureTestingModule({
    providers: [{ provide: PortfolioApiService, useValue: api }],
  });
  const fixture = TestBed.createComponent(AccountImport);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('accountId', 'a1');
  fixture.detectChanges();
  return fixture.componentInstance as any;
}

describe('AccountImport', () => {
  it('imports a file and shows its summary', () => {
    const api = new MockApi();
    const cmp = setup(api);
    const file = new File(['data'], 'statement.qfx');
    cmp.onFileSelected(file);

    expect(api.file).toBe(file);
    expect(cmp.result()?.accounts[0].inserted).toBe(3);
    expect(cmp.fileName()).toBe('statement.qfx');
    expect(cmp.error()).toBeNull();
  });

  it('auto-detects by default (no source-system override) and lists parser format options', () => {
    const api = new MockApi();
    const cmp = setup(api);
    // Auto-detect entry plus one option per parser.
    expect(cmp.formatOptions().map((o: { value: string }) => o.value)).toEqual(['', 'QFX', 'VANGUARD']);
    expect(cmp.acceptAttr()).toBe('.qfx,.ofx,.xlsx,.xls');

    cmp.onFileSelected(new File(['data'], 'statement.qfx'));
    expect(api.sourceSystem).toBeUndefined();
  });

  it('forwards the chosen format as the source-system override', () => {
    const api = new MockApi();
    const cmp = setup(api);
    cmp.sourceSystem.set('VANGUARD');
    cmp.onFileSelected(new File(['data'], 'report.xlsx'));

    expect(api.sourceSystem).toBe('VANGUARD');
  });

  it('explains a 422 as a file for other accounts, naming them, that belongs on the Accounts page', () => {
    const api = new MockApi();
    api.result = throwError(
      () => new HttpErrorResponse({ status: 422, error: { status: PortfolioImportStatus.AccountMismatch, fileAccountNumbers: ['…1111'] } }),
    );
    const cmp = setup(api);
    cmp.onFileSelected(new File(['data'], 'multi.qfx'));

    expect(cmp.error()).toContain('different account (…1111)');
    expect(cmp.error()).toContain('Accounts page');
    expect(cmp.result()).toBeNull();
  });

  it('says so when the same file was already imported, showing the earlier result', () => {
    const api = new MockApi();
    api.result = of({ ...RESULT, status: PortfolioImportStatus.AlreadyImported });
    const fixture = TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }] })
      .createComponent(AccountImport);
    fixture.componentRef.setInput('portfolioId', 'p1');
    fixture.componentRef.setInput('accountId', 'a1');
    fixture.detectChanges();

    (fixture.componentInstance as any).onFileSelected(new File(['data'], 'statement.qfx'));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('already imported on');
  });

  describe('a file that looks like another account\'s', () => {
    const misfit = importResult({
      status: PortfolioImportStatus.LikelyOtherAccount,
      accounts: [],
      selections: [
        selection({
          reason: 'LikelyOtherAccount',
          suggestedAccountId: 'a2',
          candidates: [
            candidate({ accountId: 'a2', name: 'Taxable', matchingRows: 2514, sharedTickers: 3 }),
            candidate({ accountId: 'a1', name: 'IRA', matchingRows: 0 }),
          ],
        }),
      ],
    });

    it('holds it back and explains, with nothing imported', () => {
      const api = new MockApi();
      api.result = of(misfit);
      const fixture = TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }] })
        .createComponent(AccountImport);
      fixture.componentRef.setInput('portfolioId', 'p1');
      fixture.componentRef.setInput('accountId', 'a1');
      fixture.detectChanges();

      (fixture.componentInstance as any).onFileSelected(new File(['data'], 'taxable.xlsx'));
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).querySelector('.misfit')?.textContent ?? '';
      expect(text).toContain('taxable.xlsx looks like it belongs to Taxable');
      expect(text).toContain('2,514 matching transactions · 3 shared funds in Taxable');
      expect(text).toContain('0 here');
      expect((fixture.componentInstance as any).result()).toBeNull();
    });

    it('imports it into the account it belongs to', () => {
      const api = new MockApi();
      api.result = of(misfit);
      api.nextResults = [of(importResult({ accounts: [] }))];
      const cmp = setup(api);

      cmp.onFileSelected(new File(['data'], 'taxable.xlsx'));
      cmp.importIntoSuggested();

      expect(api.calls[1]).toEqual({ accountId: 'a2', ignoreRoutingCheck: true });
      expect(cmp.elsewhere()).toBe('Taxable');
      expect(cmp.misfit()).toBeNull();
    });

    it('imports it here anyway when asked', () => {
      const api = new MockApi();
      api.result = of(misfit);
      api.nextResults = [of(RESULT)];
      const cmp = setup(api);

      cmp.onFileSelected(new File(['data'], 'taxable.xlsx'));
      cmp.importHereAnyway();

      expect(api.calls[1]).toEqual({ accountId: 'a1', ignoreRoutingCheck: true });
      expect(cmp.result()).toEqual(RESULT);
      expect(cmp.elsewhere()).toBeNull();
    });
  });
});
