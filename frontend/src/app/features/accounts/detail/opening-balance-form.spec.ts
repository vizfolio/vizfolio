import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { OpeningBalanceResponse } from '../../../core/api/models/coverage.models';
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

class MockApi {
  result: Observable<OpeningBalanceResponse> = of(RESPONSE);
  args: unknown = null;
  setOpeningBalance(portfolioId: string, accountId: string, body: unknown) {
    this.args = { portfolioId, accountId, body };
    return this.result;
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
