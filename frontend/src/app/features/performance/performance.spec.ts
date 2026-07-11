import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import {
  PortfolioPerformance,
  PortfolioSummary,
} from '../../core/api/models/performance.models';
import { Performance } from './performance';

const PERF: PortfolioPerformance = {
  from: '2025-01-01',
  to: '2025-12-31',
  startingBalance: { value: 1000, isComplete: true, snapshotAsOf: '2025-01-01', holdingsCovered: 2, holdingsMissingSnapshot: 0 },
  endingBalance: { value: 1500, isComplete: false, snapshotAsOf: '2025-12-31', holdingsCovered: 2, holdingsMissingSnapshot: 1 },
  contributions: { net: 300, deposits: 400, withdrawals: -100, count: 5 },
  returns: {
    timeWeighted: { rate: 0.15, method: 'ModifiedDietz', basis: 'Period', reason: null },
    moneyWeighted: { rate: null, method: 'XIRR', basis: 'Annualized', reason: 'NoSignChange' },
  },
  currencyCode: 'USD',
};

class MockApi {
  portfolios: Observable<PortfolioSummary[]> = of([
    { portfolioId: 'p1', name: 'Retirement', createdAt: '2026-01-01T00:00:00Z', accountCount: 2 },
  ]);
  performance: Observable<PortfolioPerformance> = of(PERF);
  fromArg: string | undefined;
  getPortfolios() {
    return this.portfolios;
  }
  getPerformance(_pid: string, from?: string) {
    this.fromArg = from;
    return this.performance;
  }
}

function setup(api: MockApi): { fixture: ComponentFixture<Performance>; cmp: any } {
  TestBed.configureTestingModule({
    imports: [Performance],
    providers: [{ provide: PortfolioApiService, useValue: api }, provideRouter([])],
  });
  const fixture = TestBed.createComponent(Performance);
  fixture.detectChanges();
  TestBed.tick();
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any };
}

describe('Performance page', () => {
  beforeEach(() => localStorage.clear());

  it('loads performance for the active portfolio', () => {
    const { cmp, fixture } = setup(new MockApi());
    expect(cmp.status()).toBe('ready');
    expect(cmp.portfolioName()).toBe('Retirement');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-performance-summary')).toBeTruthy();
  });

  it('refetches when the date range changes', () => {
    const api = new MockApi();
    const { cmp } = setup(api);
    cmp.from.set('2025-06-01');
    TestBed.tick();
    expect(api.fromArg).toBe('2025-06-01');
  });

  it('prompts to pick a portfolio when none is active', () => {
    const api = new MockApi();
    api.portfolios = of([]);
    const { cmp } = setup(api);
    expect(cmp.status()).toBe('no-portfolio');
  });

  it('shows an error when performance cannot be loaded', () => {
    const api = new MockApi();
    api.performance = throwError(() => new Error('boom'));
    const { cmp } = setup(api);
    expect(cmp.status()).toBe('error');
  });
});
