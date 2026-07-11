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
    },
  ],
  duration: 'PT0.1S',
};

class MockApi {
  result: Observable<PortfolioImportResult> = of(RESULT);
  file: File | null = null;
  sourceSystem: string | undefined;
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

  it('explains a 422 as a multi-account file that belongs on the Accounts page', () => {
    const api = new MockApi();
    api.result = throwError(() => new HttpErrorResponse({ status: 422 }));
    const cmp = setup(api);
    cmp.onFileSelected(new File(['data'], 'multi.qfx'));

    expect(cmp.error()).toContain('multiple accounts');
    expect(cmp.result()).toBeNull();
  });
});
