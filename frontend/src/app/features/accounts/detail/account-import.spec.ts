import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  ImportParser,
  PortfolioImportResult,
  PortfolioImportStatus,
} from '../../../core/api/models/imports.models';
import { AccountImport } from './account-import';

const PARSERS: ImportParser[] = [
  { sourceSystem: 'QFX', displayName: 'OFX / QFX statement', fileExtensions: ['.qfx', '.ofx'] },
  { sourceSystem: 'VANGUARD', displayName: 'Vanguard transaction report', fileExtensions: ['.xlsx', '.xls'] },
];

const RESULT: PortfolioImportResult = {
  status: PortfolioImportStatus.Success,
  sourceSystem: 'QFX',
  accounts: [
    {
      accountId: 'a1',
      created: false,
      institutionCode: 'fidelity.com',
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
    },
  ],
  duration: 'PT0.1S',
  importBatchId: 'b1',
  importedAt: '2026-10-03T16:05:00Z',
  warnings: [],
  fileAccountNumbers: [],
};

class MockApi {
  result: Observable<PortfolioImportResult> = of(RESULT);
  file: File | null = null;
  sourceSystem: string | undefined;
  getImports() {
    return of({ imports: [], transactionsImportedBeforeHistory: 0 });
  }
  getImportParsers() {
    return of(PARSERS);
  }
  importAccountFile(_pid: string, _aid: string, file: File, sourceSystem?: string) {
    this.file = file;
    this.sourceSystem = sourceSystem;
    return this.result;
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
  it('imports a file and exposes the single-account result', () => {
    const api = new MockApi();
    const cmp = setup(api);
    const file = new File(['data'], 'statement.qfx');
    cmp.onFileSelected(file);

    expect(api.file).toBe(file);
    expect(cmp.accountResult()?.inserted).toBe(3);
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
});
