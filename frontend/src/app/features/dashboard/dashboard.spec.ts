import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import {
  AccountSummary,
  PortfolioPerformance,
  PortfolioSummary,
} from '../../core/api/models/performance.models';
import { Dashboard } from './dashboard';
import { SAMPLE_PERFORMANCE } from './dashboard.util';

const PERF: PortfolioPerformance = {
  from: '2025-01-01',
  to: '2025-12-31',
  startingBalance: {
    value: 10000,
    isComplete: true,
    snapshotAsOf: '2025-01-01',
    holdingsCovered: 1,
    holdingsMissingSnapshot: 0,
  },
  endingBalance: {
    value: 25000,
    isComplete: true,
    snapshotAsOf: '2025-12-31',
    holdingsCovered: 1,
    holdingsMissingSnapshot: 0,
  },
  contributions: { net: 5000, deposits: 6000, withdrawals: -1000, count: 3 },
  returns: {
    timeWeighted: { rate: 0.2, method: 'DailyValuedTWR', basis: 'Period', reason: null, annualizedRate: null, fallbackReason: null },
    moneyWeighted: { rate: 0.22, method: 'XIRR', basis: 'Annualized', reason: null, annualizedRate: null, fallbackReason: null },
  },
  currencyCode: 'USD',
  series: { interval: 'Monthly', points: [] },
};

const PORTFOLIO: PortfolioSummary = {
  portfolioId: 'p1',
  name: 'My Portfolio',
  createdAt: '2025-01-01T00:00:00Z',
  accountCount: 2,
};

const ACCOUNT: AccountSummary = {
  accountId: 'a1',
  portfolioId: 'p1',
  name: 'Brokerage',
  institutionCode: 'vanguard.com',
  accountNumber: '1111',
  accountType: null,
  createdAt: '2025-01-01T00:00:00Z',
  transactionCount: 10,
};

class MockApi {
  portfolios: Observable<PortfolioSummary[]> = of([PORTFOLIO]);
  performance: Observable<PortfolioPerformance> = of(PERF);
  accounts: AccountSummary[] = [ACCOUNT];
  getPortfolios() {
    return this.portfolios;
  }
  getAccounts() {
    return of(this.accounts);
  }
  getImportParsers() {
    return of([]);
  }
  getPerformance() {
    return this.performance;
  }
}

/**
 * Builds the Dashboard with a mocked API. `TestBed.tick()` flushes the effect behind
 * `toObservable(activeId)` (which emits asynchronously) without rendering the template —
 * keeping the Chart.js child out of jsdom.
 */
function createDashboard(api: MockApi) {
  TestBed.configureTestingModule({
    imports: [Dashboard],
    providers: [
      { provide: PortfolioApiService, useValue: api },
      provideRouter([]),
    ],
  });
  const cmp = TestBed.createComponent(Dashboard).componentInstance as any;
  TestBed.tick();
  return cmp;
}

describe('Dashboard', () => {
  beforeEach(() => localStorage.clear());

  it('feeds the active portfolio performance to the headline row and charts', () => {
    const cmp = createDashboard(new MockApi());

    expect(cmp.status()).toBe('ready');
    expect(cmp.portfolioName()).toBe('My Portfolio');
    expect(cmp.notice()).toBeNull();
    expect(cmp.chartDatasets().length).toBe(3);
    // The shared headline row (see PerformanceHeadline) and the returns chart both render this.
    expect(cmp.displayPerformance()).toBe(PERF);
  });

  it('shows the first-run empty state when there are no portfolios', () => {
    const api = new MockApi();
    api.portfolios = of([]);
    const cmp = createDashboard(api);

    expect(cmp.status()).toBe('empty');
    expect(cmp.portfolioName()).toBeNull();
    expect(cmp.notice()).toBeNull();
  });

  it('falls back to sample data when the portfolio list cannot be loaded', () => {
    const api = new MockApi();
    api.portfolios = throwError(() => new Error('boom'));
    const cmp = createDashboard(api);

    expect(cmp.status()).toBe('error');
    expect(cmp.notice()).toContain("Couldn't reach the API");
    expect(cmp.displayPerformance()).toBe(SAMPLE_PERFORMANCE);
  });

  it('falls back to sample data when the performance request fails', () => {
    const api = new MockApi();
    api.performance = throwError(() => new Error('nope'));
    const cmp = createDashboard(api);

    expect(cmp.status()).toBe('error');
    expect(cmp.notice()).toContain("Couldn't reach the API");
  });

  it('offers the first import when the portfolio has no accounts yet', () => {
    const api = new MockApi();
    api.accounts = [];
    const cmp = createDashboard(api);

    expect(cmp.needsFirstImport()).toBe(true);
  });

  it('does not offer it once the portfolio has accounts', () => {
    const cmp = createDashboard(new MockApi());

    expect(cmp.needsFirstImport()).toBe(false);
  });
});
