import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  HistoryCoverageResponse,
  OpeningBalanceResponse,
} from '../../../core/api/models/coverage.models';
import { HoldingRow } from '../../../core/api/models/holdings.models';
import { OpeningBalanceForm } from './opening-balance-form';

const RESPONSE: OpeningBalanceResponse = {
  accountId: 'a1',
  asOf: '2024-01-01',
  snapshotsCreated: 1,
  snapshotsUpdated: 0,
  holdings: [
    {
      symbol: 'AAPL',
      accountHoldingId: 'h1',
      quantity: 10,
      marketValue: 1000,
      unitPrice: 100,
      costBasis: 900,
      currencyCode: 'USD',
      created: true,
    },
  ],
};

const COVERAGE: HistoryCoverageResponse = {
  accountId: 'a1',
  firstTransactionDate: '2023-06-02',
  earliestSnapshotDate: '2024-01-01',
  hasHistoryGap: true,
  suggestedOpeningDate: '2023-06-01',
  openingBalanceSnapshotCount: 0,
  statementSnapshotCount: 1,
  brokerPositionSnapshotCount: 0,
};

function holding(overrides: Partial<HoldingRow>): HoldingRow {
  return {
    accountHoldingId: 'h1',
    kind: 'Security',
    symbol: 'AAPL',
    name: 'Apple Inc.',
    cusip: '037833100',
    isin: null,
    currencyCode: 'USD',
    hasSnapshot: false,
    snapshotAsOf: null,
    source: null,
    quantity: null,
    unitPrice: null,
    marketValue: null,
    costBasis: null,
    gainLoss: null,
    ...overrides,
  };
}

class MockApi {
  result: Observable<OpeningBalanceResponse> = of(RESPONSE);
  coverage: Observable<HistoryCoverageResponse> = of(COVERAGE);
  holdings: Observable<HoldingRow[]> = of([]);
  holdingsAsOf: string | undefined;
  args: unknown = null;

  setOpeningBalance(portfolioId: string, accountId: string, body: unknown) {
    this.args = { portfolioId, accountId, body };
    return this.result;
  }

  getHistoryCoverage(_portfolioId: string, _accountId: string) {
    return this.coverage;
  }

  getAccountHoldings(_portfolioId: string, _accountId: string, asOf?: string) {
    this.holdingsAsOf = asOf;
    return this.holdings;
  }
}

function setup(api: MockApi) {
  TestBed.configureTestingModule({
    providers: [{ provide: PortfolioApiService, useValue: api }],
  });
  const fixture = TestBed.createComponent(OpeningBalanceForm);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('accountId', 'a1');
  fixture.detectChanges();
  return fixture.componentInstance as any;
}

describe('OpeningBalanceForm', () => {
  it('adds and removes holding rows but keeps at least one', () => {
    const cmp = setup(new MockApi());
    expect(cmp.rows().length).toBe(1);
    cmp.addRow();
    expect(cmp.rows().length).toBe(2);
    const firstKey = cmp.rows()[0].key;
    cmp.removeRow(firstKey);
    expect(cmp.rows().length).toBe(1);
    cmp.removeRow(cmp.rows()[0].key);
    expect(cmp.rows().length).toBe(1);
  });

  it('cannot submit without a date and a valid holding', () => {
    const api = new MockApi();
    const cmp = setup(api);
    expect(cmp.canSubmit()).toBe(false);

    cmp.asOf.set('2024-01-01');
    expect(cmp.canSubmit()).toBe(false); // still no valid holding

    cmp.rows.set([{ key: 0, symbol: 'AAPL', units: '10', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' }]);
    expect(cmp.canSubmit()).toBe(true);
  });

  it('submits only rows with a symbol, parsing numbers and nulling blanks', () => {
    const api = new MockApi();
    const cmp = setup(api);
    cmp.asOf.set('2024-01-01');
    cmp.defaultCurrency.set('USD');
    cmp.rows.set([
      { key: 0, symbol: 'AAPL', units: '10', marketValue: '1000', unitPrice: '', costBasis: '900', currencyCode: '', cusip: '037833100' },
      { key: 1, symbol: '', units: '5', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' },
    ]);

    cmp.submit();

    expect(api.args).toEqual({
      portfolioId: 'p1',
      accountId: 'a1',
      body: {
        asOf: '2024-01-01',
        defaultCurrencyCode: 'USD',
        holdings: [
          {
            symbol: 'AAPL',
            units: 10,
            marketValue: 1000,
            unitPrice: null,
            costBasis: 900,
            currencyCode: null,
            cusip: '037833100',
          },
        ],
      },
    });
    expect(cmp.result()?.snapshotsCreated).toBe(1);
  });

  it('prefills the as-of date and a row per holding, carrying existing snapshot values', () => {
    const api = new MockApi();
    api.holdings = of([
      // Already valued at the opening date.
      holding({
        symbol: 'AAPL',
        currencyCode: 'USD',
        cusip: '037833100',
        hasSnapshot: true,
        quantity: 10,
        marketValue: 1000,
        unitPrice: 100,
        costBasis: 900,
      }),
      // No snapshot yet — identity only, values blank.
      holding({ symbol: 'MSFT', currencyCode: 'CAD', cusip: '594918104' }),
    ]);
    const cmp = setup(api);

    expect(cmp.asOf()).toBe('2023-06-01');
    expect(api.holdingsAsOf).toBe('2023-06-01'); // holdings valued at the suggested date
    expect(cmp.rows().length).toBe(2);
    const [aapl, msft] = cmp.rows();
    expect(aapl.symbol).toBe('AAPL');
    expect(aapl.units).toBe('10');
    expect(aapl.marketValue).toBe('1000');
    expect(aapl.unitPrice).toBe('100');
    expect(aapl.costBasis).toBe('900');
    expect(msft.symbol).toBe('MSFT');
    expect(msft.units).toBe('');
    expect(msft.marketValue).toBe('');
    expect(cmp.prefilling()).toBe(false);
  });

  it('marks a row complete only when it has a usable market value', () => {
    const cmp = setup(new MockApi());
    const base = { key: 0, symbol: 'AAPL', units: '10', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' };

    // Units only → incomplete (no way to value it).
    expect(cmp.isRowComplete({ ...base })).toBe(false);
    // Explicit market value → complete.
    expect(cmp.isRowComplete({ ...base, marketValue: '1000' })).toBe(true);
    // Unit price (market value derived from units × price) → complete.
    expect(cmp.isRowComplete({ ...base, unitPrice: '100' })).toBe(true);
    // No symbol or no units → incomplete.
    expect(cmp.isRowComplete({ ...base, symbol: '', marketValue: '1000' })).toBe(false);
    expect(cmp.isRowComplete({ ...base, units: '', marketValue: '1000' })).toBe(false);
  });

  it('tallies complete holdings, ignoring blank scratch rows', () => {
    const cmp = setup(new MockApi());
    cmp.rows.set([
      { key: 0, symbol: 'AAPL', units: '10', marketValue: '1000', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' },
      { key: 1, symbol: 'MSFT', units: '5', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' },
      { key: 2, symbol: '', units: '', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' },
    ]);

    expect(cmp.namedRows().length).toBe(2); // blank row excluded
    expect(cmp.completeCount()).toBe(1); // only AAPL is complete
  });

  it('falls back to a single blank row when there is no suggested date', () => {
    const api = new MockApi();
    api.coverage = of({ ...COVERAGE, suggestedOpeningDate: null });
    api.holdings = of([holding({ symbol: 'AAPL' })]);
    const cmp = setup(api);

    expect(cmp.asOf()).toBe('');
    expect(api.holdingsAsOf).toBeUndefined(); // holdings not fetched without a date
    expect(cmp.rows().length).toBe(1);
    expect(cmp.rows()[0].symbol).toBe('');
  });

  it('falls back to a single blank row when the account has no holdings', () => {
    const api = new MockApi();
    api.holdings = of([]);
    const cmp = setup(api);

    expect(cmp.rows().length).toBe(1);
    expect(cmp.rows()[0].symbol).toBe('');
  });

  it('stays usable when the prefetch fails', () => {
    const api = new MockApi();
    api.coverage = throwError(() => new HttpErrorResponse({ status: 500 }));
    const cmp = setup(api);

    expect(cmp.asOf()).toBe('');
    expect(cmp.rows().length).toBe(1);
    expect(cmp.prefilling()).toBe(false);

    cmp.asOf.set('2024-01-01');
    cmp.rows.set([{ key: 0, symbol: 'AAPL', units: '10', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' }]);
    expect(cmp.canSubmit()).toBe(true);
  });

  it('surfaces a 400 as a validation message', () => {
    const api = new MockApi();
    api.result = throwError(() => new HttpErrorResponse({ status: 400 }));
    const cmp = setup(api);
    cmp.asOf.set('2024-01-01');
    cmp.rows.set([{ key: 0, symbol: 'AAPL', units: '10', marketValue: '', unitPrice: '', costBasis: '', currencyCode: '', cusip: '' }]);
    cmp.submit();
    expect(cmp.error()).toContain('at least one holding');
    expect(cmp.submitting()).toBe(false);
  });
});
