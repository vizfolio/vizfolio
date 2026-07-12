import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ActivePortfolioService } from './active-portfolio.service';
import { PortfolioSummary } from '../api/models/performance.models';

function portfolio(id: string, name = id): PortfolioSummary {
  return { portfolioId: id, name, createdAt: '2026-01-01T00:00:00Z', accountCount: 0 };
}

/** Injects the service (which fetches on construction) and flushes the portfolios request. */
function bootstrap(response: PortfolioSummary[] | 'error'): {
  service: ActivePortfolioService;
  http: HttpTestingController;
} {
  const service = TestBed.inject(ActivePortfolioService);
  const http = TestBed.inject(HttpTestingController);
  const req = http.expectOne('/api/portfolios');
  if (response === 'error') {
    req.flush('boom', { status: 500, statusText: 'Server Error' });
  } else {
    req.flush(response);
  }
  return { service, http };
}

describe('ActivePortfolioService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('defaults the active portfolio to the first when nothing is stored', () => {
    const { service, http } = bootstrap([portfolio('p1'), portfolio('p2')]);
    expect(service.activeId()).toBe('p1');
    expect(service.active()?.portfolioId).toBe('p1');
    expect(service.status()).toBe('ready');
    http.verify();
  });

  it('honours a stored selection that still exists', () => {
    localStorage.setItem('vizfolio-active-portfolio', 'p2');
    const { service, http } = bootstrap([portfolio('p1'), portfolio('p2')]);
    expect(service.activeId()).toBe('p2');
    http.verify();
  });

  it('falls back to the first portfolio when the stored id is gone', () => {
    localStorage.setItem('vizfolio-active-portfolio', 'missing');
    const { service, http } = bootstrap([portfolio('p1')]);
    expect(service.activeId()).toBe('p1');
    http.verify();
  });

  it('reports empty status and clears the selection when there are no portfolios', () => {
    const { service, http } = bootstrap([]);
    expect(service.status()).toBe('empty');
    expect(service.activeId()).toBeNull();
    expect(service.active()).toBeNull();
    http.verify();
  });

  it('reports error status when the list cannot be loaded', () => {
    const { service, http } = bootstrap('error');
    expect(service.status()).toBe('error');
    http.verify();
  });

  it('select() switches the active portfolio, ignoring unknown ids', () => {
    const { service, http } = bootstrap([portfolio('p1'), portfolio('p2')]);
    service.select('p2');
    expect(service.activeId()).toBe('p2');
    service.select('nope');
    expect(service.activeId()).toBe('p2');
    http.verify();
  });

  it('adopt() adds a new portfolio and makes it active', () => {
    const { service, http } = bootstrap([portfolio('p1')]);
    service.adopt(portfolio('p9', 'New'));
    expect(service.activeId()).toBe('p9');
    expect(service.portfolios().map((p) => p.portfolioId)).toContain('p9');
    expect(service.status()).toBe('ready');
    http.verify();
  });

  it('persists the active selection to localStorage', () => {
    const { service, http } = bootstrap([portfolio('p1'), portfolio('p2')]);
    service.select('p2');
    TestBed.tick();
    expect(localStorage.getItem('vizfolio-active-portfolio')).toBe('p2');
    http.verify();
  });
});
