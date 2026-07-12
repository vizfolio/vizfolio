import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { HoldingRow } from '../../../core/api/models/holdings.models';
import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { AccountHoldings } from './account-holdings';

function holding(overrides: Partial<HoldingRow> = {}): HoldingRow {
  return {
    accountHoldingId: 'h1',
    kind: 'Security',
    symbol: 'VOO',
    name: 'Vanguard S&P 500 ETF',
    cusip: null,
    isin: null,
    currencyCode: 'USD',
    hasSnapshot: true,
    snapshotAsOf: '2026-06-01',
    source: 'BrokerPosition',
    quantity: 10,
    unitPrice: 525.5,
    marketValue: 5255,
    costBasis: 5000,
    gainLoss: 255,
    ...overrides,
  };
}

class MockApi {
  holdings: Observable<HoldingRow[]> = of([holding()]);
  getAccountHoldings() {
    return this.holdings;
  }
}

function setup(api: MockApi): AccountHoldings {
  TestBed.configureTestingModule({
    providers: [{ provide: PortfolioApiService, useValue: api }],
  });
  const fixture = TestBed.createComponent(AccountHoldings);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('accountId', 'a1');
  TestBed.tick(); // Flush the toObservable(query) effect.
  return fixture.componentInstance as unknown as AccountHoldings;
}

describe('AccountHoldings', () => {
  it('loads holdings and totals their market value', () => {
    const cmp = setup(new MockApi()) as any;
    expect(cmp.status()).toBe('ready');
    expect(cmp.holdings().length).toBe(1);
    expect(cmp.totalValue()).toBe(5255);
    expect(cmp.totalLabel()).toContain('5,255');
    expect(cmp.missingCount()).toBe(0);
  });

  it('counts holdings that lack a snapshot', () => {
    const api = new MockApi();
    api.holdings = of([
      holding(),
      holding({ accountHoldingId: 'h2', symbol: 'AAPL', hasSnapshot: false, marketValue: null }),
    ]);
    const cmp = setup(api) as any;
    expect(cmp.missingCount()).toBe(1);
    expect(cmp.totalValue()).toBe(5255); // Unvalued holding contributes nothing.
  });

  it('surfaces an error status when the API fails', () => {
    const api = new MockApi();
    api.holdings = throwError(() => new Error('boom'));
    const cmp = setup(api) as any;
    expect(cmp.status()).toBe('error');
  });
});
