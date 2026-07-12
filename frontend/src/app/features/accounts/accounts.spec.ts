import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import {
  ImportParser,
  PortfolioImportResult,
  PortfolioImportStatus,
} from '../../core/api/models/imports.models';
import {
  AccountSummary,
  PortfolioSummary,
} from '../../core/api/models/performance.models';
import { Accounts } from './accounts';

function portfolio(id: string): PortfolioSummary {
  return { portfolioId: id, name: 'Retirement', createdAt: '2026-01-01T00:00:00Z', accountCount: 1 };
}

function account(id: string, name: string): AccountSummary {
  return {
    accountId: id,
    portfolioId: 'p1',
    name,
    institutionCode: 'fidelity.com',
    accountNumber: id,
    accountType: 'Brokerage',
    createdAt: '2026-01-01T00:00:00Z',
    transactionCount: 0,
  };
}

const IMPORT_RESULT: PortfolioImportResult = {
  status: PortfolioImportStatus.Success,
  sourceSystem: 'QFX',
  accounts: [
    {
      accountId: 'a2',
      created: true,
      institutionCode: 'vanguard.com',
      accountNumber: 'E2E-AAA',
      considered: 5,
      inserted: 5,
      skipped: 0,
      failed: 0,
      failures: [],
    },
  ],
  duration: 'PT0.2S',
};

const PARSERS: ImportParser[] = [
  { sourceSystem: 'QFX', displayName: 'OFX / QFX statement', fileExtensions: ['.qfx', '.ofx'] },
  { sourceSystem: 'VANGUARD', displayName: 'Vanguard transaction report', fileExtensions: ['.xlsx', '.xls'] },
];

class MockApi {
  portfolios: Observable<PortfolioSummary[]> = of([portfolio('p1')]);
  accountsList: AccountSummary[] = [account('a1', 'Brokerage')];
  createResult: Observable<AccountSummary> = of(account('aNew', 'New Account'));
  importResult: Observable<PortfolioImportResult> = of(IMPORT_RESULT);
  createBody: unknown = null;
  importedFile: File | null = null;
  sourceSystem: string | undefined;

  getPortfolios() {
    return this.portfolios;
  }
  getImportParsers() {
    return of(PARSERS);
  }
  getAccounts() {
    return of(this.accountsList);
  }
  createAccount(_pid: string, body: unknown) {
    this.createBody = body;
    return this.createResult;
  }
  importPortfolioFile(_pid: string, file: File, sourceSystem?: string) {
    this.importedFile = file;
    this.sourceSystem = sourceSystem;
    return this.importResult;
  }
}

function setup(api: MockApi): {
  fixture: ComponentFixture<Accounts>;
  cmp: any;
} {
  TestBed.configureTestingModule({
    imports: [Accounts],
    providers: [{ provide: PortfolioApiService, useValue: api }, provideRouter([])],
  });
  const fixture = TestBed.createComponent(Accounts);
  fixture.detectChanges();
  TestBed.tick();
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any };
}

describe('Accounts', () => {
  beforeEach(() => localStorage.clear());

  it('loads and lists accounts for the active portfolio', () => {
    const { fixture, cmp } = setup(new MockApi());
    expect(cmp.listStatus()).toBe('ready');
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Brokerage');
  });

  it('creates an account, prepends it, and resets the form', () => {
    const api = new MockApi();
    const { cmp } = setup(api);

    cmp.form.set({
      name: 'Roth',
      institutionCode: 'Vanguard.com',
      accountNumber: '9001',
      accountType: '',
    });
    cmp.create();

    expect(api.createBody).toEqual({
      name: 'Roth',
      institutionCode: 'Vanguard.com',
      accountNumber: '9001',
      accountType: null,
    });
    expect(cmp.accounts()[0].accountId).toBe('aNew');
    expect(cmp.form().name).toBe('');
  });

  it('surfaces a 409 as a duplicate-account message', () => {
    const api = new MockApi();
    api.createResult = throwError(() => new HttpErrorResponse({ status: 409 }));
    const { cmp } = setup(api);

    cmp.form.set({
      name: 'Dup',
      institutionCode: 'fidelity.com',
      accountNumber: 'a1',
      accountType: '',
    });
    cmp.create();

    expect(cmp.formError()).toContain('already exists');
    expect(cmp.submitting()).toBe(false);
  });

  it('shows an import result and reloads accounts on success', () => {
    const api = new MockApi();
    const { cmp } = setup(api);

    // The server would have created a2 during import; reflect that in the reload.
    api.accountsList = [account('a1', 'Brokerage'), account('a2', 'Imported')];
    const file = new File(['data'], 'multi.qfx');
    cmp.onFileSelected(file);

    expect(api.importedFile).toBe(file);
    expect(api.sourceSystem).toBeUndefined(); // auto-detect by default
    expect(cmp.importResult()?.accounts.length).toBe(1);
    expect(cmp.accounts().length).toBe(2);
  });

  it('forwards the chosen format as the source-system override', () => {
    const api = new MockApi();
    const { cmp } = setup(api);

    cmp.sourceSystem.set('VANGUARD');
    cmp.onFileSelected(new File(['data'], 'report.xlsx'));

    expect(api.sourceSystem).toBe('VANGUARD');
  });

  it('maps an unsupported-format import error to guidance', () => {
    const api = new MockApi();
    api.importResult = throwError(() => new HttpErrorResponse({ status: 415 }));
    const { cmp } = setup(api);

    cmp.onFileSelected(new File(['data'], 'notes.txt'));

    expect(cmp.importError()).toContain('Unsupported file type');
    expect(cmp.importResult()).toBeNull();
  });

  it('prompts to pick a portfolio when none is active', () => {
    const api = new MockApi();
    api.portfolios = of([]);
    const { fixture, cmp } = setup(api);

    expect(cmp.activeId()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'No portfolio selected',
    );
  });
});
