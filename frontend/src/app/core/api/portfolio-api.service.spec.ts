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

  it('toApiDate formats a Date as YYYY-MM-DD', () => {
    expect(toApiDate(new Date('2026-03-09T12:00:00Z'))).toBe('2026-03-09');
  });
});
