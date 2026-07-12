import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  AccountSummary,
  PortfolioPerformance,
  PortfolioSummary,
} from '../../../core/api/models/performance.models';
import { Account } from './account';

const PERF: PortfolioPerformance = {
  from: '2025-01-01',
  to: '2025-12-31',
  startingBalance: { value: 100, isComplete: true, snapshotAsOf: '2025-01-01', holdingsCovered: 1, holdingsMissingSnapshot: 0 },
  endingBalance: { value: 200, isComplete: true, snapshotAsOf: '2025-12-31', holdingsCovered: 1, holdingsMissingSnapshot: 0 },
  contributions: { net: 50, deposits: 60, withdrawals: -10, count: 2 },
  returns: {
    timeWeighted: { rate: 0.1, method: 'ModifiedDietz', basis: 'Period', reason: null },
    moneyWeighted: { rate: 0.12, method: 'XIRR', basis: 'Annualized', reason: null },
  },
  currencyCode: 'USD',
};

function summary(): AccountSummary {
  return {
    accountId: 'a1',
    portfolioId: 'p1',
    name: 'Brokerage',
    institutionCode: 'fidelity.com',
    accountNumber: '1234',
    accountType: 'Roth IRA',
    createdAt: '2026-01-01T00:00:00Z',
    transactionCount: 7,
  };
}

class MockApi {
  portfolios: Observable<PortfolioSummary[]> = of([
    { portfolioId: 'p1', name: 'Retirement', createdAt: '2026-01-01T00:00:00Z', accountCount: 1 },
  ]);
  account: Observable<AccountSummary> = of(summary());
  getPortfolios() {
    return this.portfolios;
  }
  getAccount() {
    return this.account;
  }
  getAccountPerformance() {
    return of(PERF);
  }
  getAccountHoldings() {
    return of([]);
  }
  getAccountLedger() {
    return of([]);
  }
}

function setup(api: MockApi): { fixture: ComponentFixture<Account>; cmp: any; el: HTMLElement } {
  TestBed.configureTestingModule({
    imports: [Account],
    providers: [{ provide: PortfolioApiService, useValue: api }, provideRouter([])],
  });
  const fixture = TestBed.createComponent(Account);
  fixture.componentRef.setInput('accountId', 'a1');
  fixture.detectChanges();
  TestBed.tick();
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any, el: fixture.nativeElement as HTMLElement };
}

describe('Account (detail)', () => {
  beforeEach(() => localStorage.clear());

  it('loads the account and shows its header and tabs', () => {
    const { cmp, el } = setup(new MockApi());
    expect(cmp.status()).toBe('ready');
    expect(el.querySelector('.account-header h1')?.textContent).toContain('Brokerage');
    expect(el.querySelectorAll('.tab').length).toBe(6);
  });

  it('defaults to the Performance tab', () => {
    const { cmp } = setup(new MockApi());
    expect(cmp.tab()).toBe('performance');
  });

  it('switches to the Holdings tab', () => {
    const { cmp, fixture, el } = setup(new MockApi());
    cmp.select('holdings');
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();
    expect(el.querySelector('app-account-holdings')).toBeTruthy();
  });

  it('shows a no-portfolio message when no portfolio is active', () => {
    const api = new MockApi();
    api.portfolios = of([]);
    const { cmp, el } = setup(api);
    expect(cmp.status()).toBe('no-portfolio');
    expect(el.textContent).toContain('No active portfolio');
  });
});
