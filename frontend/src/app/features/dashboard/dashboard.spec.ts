import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import {
  PortfolioPerformance,
  PortfolioSummary,
} from '../../core/api/models/performance.models';
import { Dashboard } from './dashboard';

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
    timeWeighted: { rate: 0.2, method: 'ModifiedDietz', basis: 'Period', reason: null },
    moneyWeighted: { rate: 0.22, method: 'XIRR', basis: 'Annualized', reason: null },
  },
  currencyCode: 'USD',
};

const PORTFOLIO: PortfolioSummary = {
  portfolioId: 'p1',
  name: 'My Portfolio',
  createdAt: '2025-01-01T00:00:00Z',
  accountCount: 2,
};

class MockApi {
  portfolios: Observable<PortfolioSummary[]> = of([PORTFOLIO]);
  performance: Observable<PortfolioPerformance> = of(PERF);
  getPortfolios() {
    return this.portfolios;
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

  it('maps the active portfolio performance into card signals', () => {
    const cmp = createDashboard(new MockApi());

    expect(cmp.status()).toBe('ready');
    expect(cmp.portfolioName()).toBe('My Portfolio');
    expect(cmp.valueLabel()).toContain('25,000');
    expect(cmp.depositsLabel()).toContain('6,000');
    expect(cmp.withdrawalsLabel()).toContain('1,000');
    expect(cmp.returnLabel()).toBe('+20.0%');
    expect(cmp.notice()).toBeNull();
    expect(cmp.chartDatasets().length).toBe(3);
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
    expect(cmp.valueLabel()).not.toBe('—');
  });

  it('falls back to sample data when the performance request fails', () => {
    const api = new MockApi();
    api.performance = throwError(() => new Error('nope'));
    const cmp = createDashboard(api);

    expect(cmp.status()).toBe('error');
    expect(cmp.notice()).toContain("Couldn't reach the API");
  });
});
