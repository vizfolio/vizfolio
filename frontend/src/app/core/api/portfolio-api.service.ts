import { HttpClient, HttpParams } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AccountSummary,
  PortfolioPerformance,
  PortfolioSummary,
} from './models/performance.models';

/** Base path for all API calls. The dev proxy (proxy.conf.json) forwards /api to the backend. */
const API_BASE = '/api';

/** Formats a Date as the `YYYY-MM-DD` the performance endpoints expect. */
export function toApiDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * Typed client for the portfolio/performance endpoints. Returns cold Observables;
 * callers adapt to signals (e.g. via `toSignal`) at the component edge.
 */
@Service()
export class PortfolioApiService {
  private readonly http = inject(HttpClient);

  /** GET /api/portfolios */
  getPortfolios(): Observable<PortfolioSummary[]> {
    return this.http.get<PortfolioSummary[]>(`${API_BASE}/portfolios`);
  }

  /** GET /api/portfolios/{portfolioId} */
  getPortfolio(portfolioId: string): Observable<PortfolioSummary> {
    return this.http.get<PortfolioSummary>(`${API_BASE}/portfolios/${portfolioId}`);
  }

  /** GET /api/portfolios/{portfolioId}/accounts */
  getAccounts(portfolioId: string): Observable<AccountSummary[]> {
    return this.http.get<AccountSummary[]>(
      `${API_BASE}/portfolios/${portfolioId}/accounts`,
    );
  }

  /**
   * GET /api/portfolios/{portfolioId}/performance
   * `from`/`to` are optional ISO dates (`YYYY-MM-DD`); omit to use backend defaults.
   */
  getPerformance(
    portfolioId: string,
    from?: string,
    to?: string,
  ): Observable<PortfolioPerformance> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    return this.http.get<PortfolioPerformance>(
      `${API_BASE}/portfolios/${portfolioId}/performance`,
      { params },
    );
  }
}
