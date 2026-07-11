import { HttpClient, HttpParams } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  ImportAllRequest,
  ImportAllResponse,
  ImportFundsRequest,
  ImportResult,
  ImportSecuritiesRequest,
  RelinkLedgerResponse,
} from './models/admin.models';
import {
  HistoryCoverageResponse,
  OpeningBalanceResponse,
  SetOpeningBalanceRequest,
} from './models/coverage.models';
import { ImportParser, PortfolioImportResult } from './models/imports.models';
import {
  AccountSummary,
  PortfolioPerformance,
  PortfolioSummary,
} from './models/performance.models';
import {
  CreateAccountRequest,
  CreatePortfolioRequest,
} from './models/portfolio.models';

/** Base path for all API calls. The dev proxy (proxy.conf.json) forwards /api to the backend. */
const API_BASE = '/api';

/** Formats a Date as the `YYYY-MM-DD` the performance endpoints expect. */
export function toApiDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Builds the optional from/to date query params shared by the performance endpoints. */
function dateRangeParams(from?: string, to?: string): HttpParams {
  let params = new HttpParams();
  if (from) {
    params = params.set('from', from);
  }
  if (to) {
    params = params.set('to', to);
  }
  return params;
}

/**
 * Wraps a File in the multipart form-data body the import endpoints expect. Pass `sourceSystem`
 * to force a specific parser; omit it to let the backend auto-detect the format.
 */
function fileForm(file: File, sourceSystem?: string): FormData {
  const form = new FormData();
  form.append('file', file, file.name);
  if (sourceSystem) {
    form.append('sourceSystem', sourceSystem);
  }
  return form;
}

/**
 * Typed client for the Vizfolio API. Returns cold Observables; callers adapt to signals
 * (e.g. via `toSignal`) at the component edge. All routes are served under `/api`.
 */
@Service()
export class PortfolioApiService {
  private readonly http = inject(HttpClient);

  // ---- Portfolios ----------------------------------------------------------

  /** GET /api/portfolios */
  getPortfolios(): Observable<PortfolioSummary[]> {
    return this.http.get<PortfolioSummary[]>(`${API_BASE}/portfolios`);
  }

  /** GET /api/portfolios/{portfolioId} */
  getPortfolio(portfolioId: string): Observable<PortfolioSummary> {
    return this.http.get<PortfolioSummary>(`${API_BASE}/portfolios/${portfolioId}`);
  }

  /** POST /api/portfolios */
  createPortfolio(name: string): Observable<PortfolioSummary> {
    const body: CreatePortfolioRequest = { name };
    return this.http.post<PortfolioSummary>(`${API_BASE}/portfolios`, body);
  }

  // ---- Accounts ------------------------------------------------------------

  /** GET /api/portfolios/{portfolioId}/accounts */
  getAccounts(portfolioId: string): Observable<AccountSummary[]> {
    return this.http.get<AccountSummary[]>(
      `${API_BASE}/portfolios/${portfolioId}/accounts`,
    );
  }

  /** GET /api/portfolios/{portfolioId}/accounts/{accountId} */
  getAccount(portfolioId: string, accountId: string): Observable<AccountSummary> {
    return this.http.get<AccountSummary>(
      `${API_BASE}/portfolios/${portfolioId}/accounts/${accountId}`,
    );
  }

  /** POST /api/portfolios/{portfolioId}/accounts */
  createAccount(
    portfolioId: string,
    account: Omit<CreateAccountRequest, 'portfolioId'>,
  ): Observable<AccountSummary> {
    const body: CreateAccountRequest = { portfolioId, ...account };
    return this.http.post<AccountSummary>(
      `${API_BASE}/portfolios/${portfolioId}/accounts`,
      body,
    );
  }

  // ---- Imports -------------------------------------------------------------

  /** GET /api/imports/parsers — available parsers for the "Format" override, highest priority first. */
  getImportParsers(): Observable<ImportParser[]> {
    return this.http.get<ImportParser[]>(`${API_BASE}/imports/parsers`);
  }

  /**
   * POST /api/portfolios/{portfolioId}/imports
   * Multi-account broker file (e.g. QFX carrying account metadata).
   * `sourceSystem` forces a specific parser; omit to auto-detect.
   */
  importPortfolioFile(
    portfolioId: string,
    file: File,
    sourceSystem?: string,
  ): Observable<PortfolioImportResult> {
    return this.http.post<PortfolioImportResult>(
      `${API_BASE}/portfolios/${portfolioId}/imports`,
      fileForm(file, sourceSystem),
    );
  }

  /**
   * POST /api/portfolios/{portfolioId}/accounts/{accountId}/imports
   * Single-account broker file uploaded to a specific account.
   * `sourceSystem` forces a specific parser; omit to auto-detect.
   */
  importAccountFile(
    portfolioId: string,
    accountId: string,
    file: File,
    sourceSystem?: string,
  ): Observable<PortfolioImportResult> {
    return this.http.post<PortfolioImportResult>(
      `${API_BASE}/portfolios/${portfolioId}/accounts/${accountId}/imports`,
      fileForm(file, sourceSystem),
    );
  }

  // ---- Performance ---------------------------------------------------------

  /**
   * GET /api/portfolios/{portfolioId}/performance
   * `from`/`to` are optional ISO dates (`YYYY-MM-DD`); omit to use backend defaults.
   */
  getPerformance(
    portfolioId: string,
    from?: string,
    to?: string,
  ): Observable<PortfolioPerformance> {
    return this.http.get<PortfolioPerformance>(
      `${API_BASE}/portfolios/${portfolioId}/performance`,
      { params: dateRangeParams(from, to) },
    );
  }

  /** GET /api/portfolios/{portfolioId}/accounts/{accountId}/performance */
  getAccountPerformance(
    portfolioId: string,
    accountId: string,
    from?: string,
    to?: string,
  ): Observable<PortfolioPerformance> {
    return this.http.get<PortfolioPerformance>(
      `${API_BASE}/portfolios/${portfolioId}/accounts/${accountId}/performance`,
      { params: dateRangeParams(from, to) },
    );
  }

  // ---- History & data quality ---------------------------------------------

  /** GET /api/portfolios/{portfolioId}/accounts/{accountId}/history-coverage */
  getHistoryCoverage(
    portfolioId: string,
    accountId: string,
  ): Observable<HistoryCoverageResponse> {
    return this.http.get<HistoryCoverageResponse>(
      `${API_BASE}/portfolios/${portfolioId}/accounts/${accountId}/history-coverage`,
    );
  }

  /** POST /api/portfolios/{portfolioId}/accounts/{accountId}/opening-balance */
  setOpeningBalance(
    portfolioId: string,
    accountId: string,
    request: Omit<SetOpeningBalanceRequest, 'portfolioId' | 'accountId'>,
  ): Observable<OpeningBalanceResponse> {
    const body: SetOpeningBalanceRequest = { portfolioId, accountId, ...request };
    return this.http.post<OpeningBalanceResponse>(
      `${API_BASE}/portfolios/${portfolioId}/accounts/${accountId}/opening-balance`,
      body,
    );
  }

  // ---- Admin: reference data ----------------------------------------------

  /** POST /api/admin/imports/securities */
  importSecurities(request: ImportSecuritiesRequest = {}): Observable<ImportResult> {
    return this.http.post<ImportResult>(
      `${API_BASE}/admin/imports/securities`,
      request,
    );
  }

  /** POST /api/admin/imports/funds */
  importFunds(request: ImportFundsRequest = {}): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${API_BASE}/admin/imports/funds`, request);
  }

  /** POST /api/admin/imports/all */
  importAll(request: ImportAllRequest = {}): Observable<ImportAllResponse> {
    return this.http.post<ImportAllResponse>(
      `${API_BASE}/admin/imports/all`,
      request,
    );
  }

  /** POST /api/admin/portfolios/relink */
  relinkLedger(): Observable<RelinkLedgerResponse> {
    return this.http.post<RelinkLedgerResponse>(
      `${API_BASE}/admin/portfolios/relink`,
      {},
    );
  }
}
