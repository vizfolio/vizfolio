import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { LedgerEntry } from '../../../core/api/models/ledger.models';
import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { AccountLedger } from './account-ledger';

function entry(overrides: Partial<LedgerEntry> = {}): LedgerEntry {
  return {
    accountTransactionId: 't1',
    tradeDate: '2026-06-01',
    settlementDate: '2026-06-01',
    type: 'Buy',
    sourceType: null,
    ticker: 'VOO',
    cusip: null,
    accountHoldingId: 'h1',
    holdingName: 'Vanguard S&P 500 ETF',
    quantity: 2,
    price: 500,
    amount: -1000,
    fees: 0,
    currencyCode: 'USD',
    memo: 'Buy VOO',
    ...overrides,
  };
}

class MockApi {
  ledger: Observable<LedgerEntry[]> = of([entry()]);
  readonly calls: Array<{ from?: string; to?: string }> = [];
  getAccountLedger(_portfolioId: string, _accountId: string, from?: string, to?: string) {
    this.calls.push({ from, to });
    return this.ledger;
  }
}

function setup(api: MockApi): AccountLedger {
  TestBed.configureTestingModule({
    providers: [{ provide: PortfolioApiService, useValue: api }],
  });
  const fixture = TestBed.createComponent(AccountLedger);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('accountId', 'a1');
  TestBed.tick(); // Flush the toObservable(query) effect.
  return fixture.componentInstance as unknown as AccountLedger;
}

describe('AccountLedger', () => {
  it('loads the transaction ledger', () => {
    const cmp = setup(new MockApi()) as any;
    expect(cmp.status()).toBe('ready');
    expect(cmp.entries().length).toBe(1);
    expect(cmp.entries()[0].ticker).toBe('VOO');
  });

  it('re-queries when the date filter changes', () => {
    const api = new MockApi();
    const cmp = setup(api) as any;
    api.calls.length = 0;

    cmp.from.set('2026-01-01');
    TestBed.tick();

    expect(api.calls).toContainEqual({ from: '2026-01-01', to: undefined });
  });

  it('surfaces an error status when the API fails', () => {
    const api = new MockApi();
    api.ledger = throwError(() => new Error('boom'));
    const cmp = setup(api) as any;
    expect(cmp.status()).toBe('error');
  });
});
