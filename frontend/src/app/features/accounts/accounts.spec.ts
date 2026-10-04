import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { DataHealthReport } from '../../core/api/models/health.models';
import { ImportParser } from '../../core/api/models/imports.models';
import {
  AccountSummary,
  PortfolioPerformance,
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

const PARSERS: ImportParser[] = [
  { sourceSystem: 'QFX', displayName: 'OFX / QFX statement', fileExtensions: ['.qfx', '.ofx'] },
  { sourceSystem: 'VANGUARD', displayName: 'Vanguard transaction report', fileExtensions: ['.xlsx', '.xls'] },
];

/** Just the parts of a performance response the accounts list reads. */
function performance(value: number, rate: number | null): PortfolioPerformance {
  return {
    endingBalance: { value },
    returns: { moneyWeighted: { rate } },
    currencyCode: 'USD',
  } as unknown as PortfolioPerformance;
}

const HEALTH: DataHealthReport = {
  status: 'NeedsAttention',
  currencyCode: 'USD',
  accounts: [{ accountId: 'a1', name: 'Brokerage', status: 'NeedsAttention', blocking: 1, info: 0 }],
  findings: [],
};

class MockApi {
  portfolios: Observable<PortfolioSummary[]> = of([portfolio('p1')]);
  accountsList: AccountSummary[] = [account('a1', 'Brokerage')];
  createResult: Observable<AccountSummary> = of(account('aNew', 'New Account'));
  createBody: unknown = null;
  performanceFrom: string | undefined;

  getPortfolios() {
    return this.portfolios;
  }
  getImports() {
    return of({ imports: [], transactionsImportedBeforeHistory: 0 });
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
  getAccountPerformance(_pid: string, _aid: string, from?: string) {
    this.performanceFrom = from;
    return of(performance(12345.6, 0.081));
  }
  getPortfolioHealth() {
    return of(HEALTH);
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

  it('prompts to pick a portfolio when none is active', () => {
    const api = new MockApi();
    api.portfolios = of([]);
    const { fixture, cmp } = setup(api);

    expect(cmp.activeId()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'No portfolio selected',
    );
  });

  it("shows each account's value, this year's return and a data health dot linking to its health tab", () => {
    const api = new MockApi();
    const { fixture } = setup(api);

    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr')!;
    expect(row.textContent).toContain('$12,345.60');
    expect(row.textContent).toContain('+8.1%');
    expect(api.performanceFrom).toMatch(/^\d{4}-01-01$/);
    const dot = row.querySelector('a.health-link') as HTMLAnchorElement;
    expect(dot.getAttribute('href')).toBe('/accounts/a1?tab=health');
    expect(dot.textContent).toContain('Data needs attention');
  });

  it('guides a first import when the portfolio has no accounts', () => {
    const api = new MockApi();
    api.accountsList = [];
    const { fixture } = setup(api);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-import-onboarding')).not.toBeNull();
    expect(el.querySelector('app-import-drop-zone')).not.toBeNull();
  });

  it('refreshes the list and the history after an import', () => {
    const api = new MockApi();
    const { cmp } = setup(api);

    api.accountsList = [account('a1', 'Brokerage'), account('a2', 'Imported')];
    const before = cmp.historyKey();
    cmp.onImportsChanged();

    expect(cmp.accounts().length).toBe(2);
    expect(cmp.historyKey()).toBe(before + 1);
  });
});
