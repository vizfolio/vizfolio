import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import {
  ImportAllResponse,
  ImportResult,
  RelinkLedgerResponse,
} from '../../core/api/models/admin.models';
import { Settings } from './settings';

function importResult(): ImportResult {
  return {
    considered: 10,
    upserted: 6,
    skipped: 4,
    failed: 0,
    failures: [],
    dataCleaning: [],
    duration: 'PT1.2S',
  };
}

class MockApi {
  securities: Observable<ImportResult> = of(importResult());
  funds: Observable<ImportResult> = of(importResult());
  all: Observable<ImportAllResponse> = of({
    securities: importResult(),
    funds: importResult(),
    relinked: 3,
  });
  relink: Observable<RelinkLedgerResponse> = of({ linked: 7 });

  securitiesArg: unknown = null;
  fundsArg: unknown = null;

  importSecurities(req: unknown) {
    this.securitiesArg = req;
    return this.securities;
  }
  importFunds(req: unknown) {
    this.fundsArg = req;
    return this.funds;
  }
  importAll(req: unknown) {
    return this.all;
  }
  relinkLedger() {
    return this.relink;
  }
}

function setup(api: MockApi) {
  TestBed.configureTestingModule({
    providers: [{ provide: PortfolioApiService, useValue: api }],
  });
  const fixture = TestBed.createComponent(Settings);
  fixture.detectChanges();
  return fixture.componentInstance as any;
}

describe('Settings', () => {
  beforeEach(() => localStorage.clear());

  it('runs a securities import, parsing tickers and forwarding force', () => {
    const api = new MockApi();
    const cmp = setup(api);
    cmp.secTickers.set('aapl, msft ,, ');
    cmp.secForce.set(true);
    cmp.runSecurities();

    expect(api.securitiesArg).toEqual({ tickers: ['aapl', 'msft'], force: true });
    expect(cmp.securities().status).toBe('done');
    expect(cmp.securities().result.upserted).toBe(6);
  });

  it('omits the tickers list when the field is blank', () => {
    const api = new MockApi();
    const cmp = setup(api);
    cmp.runSecurities();
    expect(api.securitiesArg).toEqual({ tickers: undefined, force: false });
  });

  it('surfaces a 409 as an import-in-progress message', () => {
    const api = new MockApi();
    api.funds = throwError(() => new HttpErrorResponse({ status: 409 }));
    const cmp = setup(api);
    cmp.runFunds();
    expect(cmp.funds().status).toBe('error');
    expect(cmp.funds().error).toContain('already in progress');
  });

  it('runs the combined import and reports its sub-results', () => {
    const cmp = setup(new MockApi());
    cmp.runAll();
    expect(cmp.all().status).toBe('done');
    expect(cmp.all().result.relinked).toBe(3);
  });

  it('runs a ledger relink', () => {
    const cmp = setup(new MockApi());
    cmp.runRelink();
    expect(cmp.relink().result.linked).toBe(7);
  });
});
