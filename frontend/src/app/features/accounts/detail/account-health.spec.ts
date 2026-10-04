import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { HistoryCoverageResponse } from '../../../core/api/models/coverage.models';
import { DataHealthReport } from '../../../core/api/models/health.models';
import { AccountHealth } from './account-health';

const REPORT: DataHealthReport = {
  status: 'Info',
  currencyCode: 'USD',
  accounts: [],
  findings: [
    {
      code: 'PreHistoryPosition', severity: 'Info', accountId: 'a1', holdingId: 'h1', symbol: 'ZXFND', from: null,
      to: '2025-01-01', message: 'We assumed you held 5 ZXFND before your first imported transaction.',
      action: { kind: 'AdjustStartingPosition', label: 'Adjust starting positions' },
      details: {
        ledgerQuantity: null, brokerQuantity: 5, amount: null, count: null, priceFetchOutcome: null,
        priceFetchMessage: null, pricesPending: false, warningCode: null, samples: [], files: [],
      },
    },
  ],
};

const COVERAGE: HistoryCoverageResponse = {
  accountId: 'a1', firstTransactionDate: '2025-01-02', earliestSnapshotDate: '2025-06-30', hasHistoryGap: false,
  suggestedOpeningDate: '2025-01-01', openingBalanceSnapshotCount: 0, statementSnapshotCount: 1,
  brokerPositionSnapshotCount: 4,
};

class MockApi {
  loads = 0;
  getAccountHealth() {
    this.loads++;
    return of(REPORT);
  }
  getHistoryCoverage() {
    return of(COVERAGE);
  }
}

function setup(api: MockApi) {
  TestBed.configureTestingModule({ providers: [{ provide: PortfolioApiService, useValue: api }, provideRouter([])] });
  const fixture = TestBed.createComponent(AccountHealth);
  fixture.componentRef.setInput('portfolioId', 'p1');
  fixture.componentRef.setInput('accountId', 'a1');
  fixture.detectChanges();
  TestBed.tick();
  fixture.detectChanges();
  return { fixture, cmp: fixture.componentInstance as any, el: fixture.nativeElement as HTMLElement };
}

describe('AccountHealth', () => {
  it("lists the account's findings and the history coverage behind them", () => {
    const { el } = setup(new MockApi());

    expect(el.textContent).toContain('We assumed you held 5 ZXFND');
    expect(el.querySelector('.coverage')?.textContent).toContain('Jan 2, 2025');
    expect(el.querySelector('.coverage')?.textContent).toContain('Statements');
    expect(el.textContent).not.toContain('History gap');
  });

  it('passes on actions that live on another tab', () => {
    const { fixture, el } = setup(new MockApi());
    const kinds: string[] = [];
    fixture.componentInstance.navigate.subscribe((k) => kinds.push(k));

    (el.querySelector('.finding button') as HTMLButtonElement).click();

    expect(kinds).toEqual(['AdjustStartingPosition']);
  });

  it('checks again after prices were requested', () => {
    const api = new MockApi();
    const { cmp } = setup(api);
    const before = api.loads;

    cmp.recheck();
    TestBed.tick();

    expect(api.loads).toBe(before + 1);
  });
});
