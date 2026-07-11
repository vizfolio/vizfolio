import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PortfolioApiService, toApiDate } from './portfolio-api.service';

describe('PortfolioApiService', () => {
  let service: PortfolioApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PortfolioApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('GETs the portfolios list', () => {
    service.getPortfolios().subscribe();
    const req = http.expectOne('/api/portfolios');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('GETs a single portfolio by id', () => {
    service.getPortfolio('abc').subscribe();
    http.expectOne('/api/portfolios/abc').flush({});
  });

  it('POSTs a new portfolio with the name in the body', () => {
    service.createPortfolio('Retirement').subscribe();
    const req = http.expectOne('/api/portfolios');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Retirement' });
    req.flush({});
  });

  it('GETs the accounts for a portfolio', () => {
    service.getAccounts('p1').subscribe();
    const req = http.expectOne('/api/portfolios/p1/accounts');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('GETs a single account', () => {
    service.getAccount('p1', 'a1').subscribe();
    http.expectOne('/api/portfolios/p1/accounts/a1').flush({});
  });

  it('POSTs a new account, injecting the route portfolioId into the body', () => {
    service
      .createAccount('p1', {
        name: 'Brokerage',
        institutionCode: 'fidelity.com',
        accountNumber: '1234',
        accountType: 'Brokerage',
      })
      .subscribe();
    const req = http.expectOne('/api/portfolios/p1/accounts');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      portfolioId: 'p1',
      name: 'Brokerage',
      institutionCode: 'fidelity.com',
      accountNumber: '1234',
      accountType: 'Brokerage',
    });
    req.flush({});
  });

  it('uploads a portfolio-scoped import as multipart form data', () => {
    const file = new File(['data'], 'multi.qfx', { type: 'application/octet-stream' });
    service.importPortfolioFile('p1', file).subscribe();
    const req = http.expectOne('/api/portfolios/p1/imports');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('file')).toBeInstanceOf(File);
    expect(((req.request.body as FormData).get('file') as File).name).toBe('multi.qfx');
    req.flush({});
  });

  it('uploads an account-scoped import as multipart form data', () => {
    const file = new File(['data'], 'single.qfx', { type: 'application/octet-stream' });
    service.importAccountFile('p1', 'a1', file).subscribe();
    const req = http.expectOne('/api/portfolios/p1/accounts/a1/imports');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).has('sourceSystem')).toBe(false);
    req.flush({});
  });

  it('includes the sourceSystem override in the import form data when provided', () => {
    const file = new File(['data'], 'report.xlsx', { type: 'application/octet-stream' });
    service.importAccountFile('p1', 'a1', file, 'VANGUARD').subscribe();
    const req = http.expectOne('/api/portfolios/p1/accounts/a1/imports');
    expect((req.request.body as FormData).get('sourceSystem')).toBe('VANGUARD');
    req.flush({});
  });

  it('GETs the available import parsers', () => {
    service.getImportParsers().subscribe();
    const req = http.expectOne('/api/imports/parsers');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('adds from/to query params to the performance request when provided', () => {
    service.getPerformance('p1', '2025-01-01', '2025-12-31').subscribe();
    const req = http.expectOne(
      (r) => r.url === '/api/portfolios/p1/performance',
    );
    expect(req.request.params.get('from')).toBe('2025-01-01');
    expect(req.request.params.get('to')).toBe('2025-12-31');
    req.flush({});
  });

  it('omits params when dates are not provided', () => {
    service.getPerformance('p1').subscribe();
    const req = http.expectOne('/api/portfolios/p1/performance');
    expect(req.request.params.keys().length).toBe(0);
    req.flush({});
  });

  it('GETs account performance with the same date-range params', () => {
    service.getAccountPerformance('p1', 'a1', '2025-01-01').subscribe();
    const req = http.expectOne(
      (r) => r.url === '/api/portfolios/p1/accounts/a1/performance',
    );
    expect(req.request.params.get('from')).toBe('2025-01-01');
    req.flush({});
  });

  it('GETs account history coverage', () => {
    service.getHistoryCoverage('p1', 'a1').subscribe();
    http.expectOne('/api/portfolios/p1/accounts/a1/history-coverage').flush({});
  });

  it('POSTs an opening balance, injecting route ids into the body', () => {
    service
      .setOpeningBalance('p1', 'a1', {
        asOf: '2024-01-01',
        holdings: [{ symbol: 'AAPL', units: 10 }],
      })
      .subscribe();
    const req = http.expectOne('/api/portfolios/p1/accounts/a1/opening-balance');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      portfolioId: 'p1',
      accountId: 'a1',
      asOf: '2024-01-01',
      holdings: [{ symbol: 'AAPL', units: 10 }],
    });
    req.flush({});
  });

  it('POSTs a securities import with the request body', () => {
    service.importSecurities({ tickers: ['AAPL'], force: true }).subscribe();
    const req = http.expectOne('/api/admin/imports/securities');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ tickers: ['AAPL'], force: true });
    req.flush({});
  });

  it('POSTs a funds import', () => {
    service.importFunds().subscribe();
    const req = http.expectOne('/api/admin/imports/funds');
    expect(req.request.body).toEqual({});
    req.flush({});
  });

  it('POSTs the combined import-all', () => {
    service.importAll({ force: true }).subscribe();
    const req = http.expectOne('/api/admin/imports/all');
    expect(req.request.body).toEqual({ force: true });
    req.flush({});
  });

  it('POSTs a ledger relink', () => {
    service.relinkLedger().subscribe();
    const req = http.expectOne('/api/admin/portfolios/relink');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('toApiDate formats a Date as YYYY-MM-DD', () => {
    expect(toApiDate(new Date('2026-03-09T12:00:00Z'))).toBe('2026-03-09');
  });
});
