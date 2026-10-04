import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import {
  OpeningBalanceResponse,
  OpeningPosition,
  OpeningPositionsResponse,
} from '../../../core/api/models/coverage.models';
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

function position(overrides: Partial<OpeningPosition>): OpeningPosition {
  return {
    accountHoldingId: 'h1',
    symbol: 'AAPL',
    quantity: 0,
    class: 'None',
    verified: true,
    unitPrice: null,
    marketValue: 0,
    ...overrides,
  };
}

const CASH_NONE = position({ accountHoldingId: null, symbol: null });

const POSITIONS: OpeningPositionsResponse = {
  asOf: '2023-06-01',
  holdings: [],
  cash: CASH_NONE,
};

class MockApi {
  result: Observable<OpeningBalanceResponse> = of(RESPONSE);
  positions: Observable<OpeningPositionsResponse> = of(POSITIONS);
  args: unknown = null;

  setOpeningBalance(portfolioId: string, accountId: string, body: unknown) {
    this.args = { portfolioId, accountId, body };
    return this.result;
  }

  getOpeningPositions(_portfolioId: string, _accountId: string) {
    return this.positions;
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

  it('prefills the opening date and the positions held before the history, from the statements', () => {
    const api = new MockApi();
    api.positions = of({
      asOf: '2023-06-01',
      holdings: [
        // Held before the imported history: prefilled with what the statement implies.
        position({ symbol: 'AAPL', quantity: 10, class: 'PreHistory', unitPrice: 100, marketValue: 1000 }),
        // The ledger and the statement disagree: shown blank for the user to supply.
        position({ accountHoldingId: 'h2', symbol: 'MSFT', quantity: -3, class: 'Inconsistent', marketValue: null }),
        // Bought within the history: not held at the opening, so not shown.
        position({ accountHoldingId: 'h3', symbol: 'VOO' }),
      ],
      cash: position({ accountHoldingId: null, symbol: null, quantity: 250, class: 'PreHistory', unitPrice: 1, marketValue: 250 }),
    });
    const cmp = setup(api);

    expect(cmp.asOf()).toBe('2023-06-01');
    expect(cmp.rows().map((r: { symbol: string }) => r.symbol)).toEqual(['AAPL', 'MSFT', '$CASH']);
    const [aapl, msft, cash] = cmp.rows();
    expect(aapl.units).toBe('10');
    expect(aapl.marketValue).toBe('1000');
    expect(aapl.unitPrice).toBe('100');
    expect(msft.units).toBe('');
    expect(msft.marketValue).toBe('');
    expect(cash.units).toBe('250');
    expect(cash.marketValue).toBe('250');
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

  it('falls back to a single blank row when the account has no transactions', () => {
    const api = new MockApi();
    api.positions = of({ asOf: null, holdings: [position({ symbol: 'AAPL' })], cash: CASH_NONE });
    const cmp = setup(api);

    expect(cmp.asOf()).toBe('');
    expect(cmp.rows().length).toBe(1);
    expect(cmp.rows()[0].symbol).toBe('');
  });

  it('falls back to a single blank row when nothing was held before the history', () => {
    const api = new MockApi();
    api.positions = of({ ...POSITIONS, holdings: [position({ symbol: 'AAPL' })] });
    const cmp = setup(api);

    expect(cmp.rows().length).toBe(1);
    expect(cmp.rows()[0].symbol).toBe('');
  });

  it('stays usable when the prefetch fails', () => {
    const api = new MockApi();
    api.positions = throwError(() => new HttpErrorResponse({ status: 500 }));
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
