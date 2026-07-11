import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { PortfolioImportResult, PortfolioImportStatus } from '../../../core/api/models/imports.models';
import { AccountImport } from './account-import';

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
  importAccountFile(_pid: string, _aid: string, file: File) {
    this.file = file;
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

  it('explains a 422 as a multi-account file that belongs on the Accounts page', () => {
    const api = new MockApi();
    api.result = throwError(() => new HttpErrorResponse({ status: 422 }));
    const cmp = setup(api);
    cmp.onFileSelected(new File(['data'], 'multi.qfx'));

    expect(cmp.error()).toContain('multiple accounts');
    expect(cmp.result()).toBeNull();
  });
});
