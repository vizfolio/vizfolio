import { DOCUMENT } from '@angular/common';
import { Service, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';

import { PortfolioApiService } from '../api/portfolio-api.service';
import { PortfolioSummary } from '../api/models/performance.models';

const STORAGE_KEY = 'vizfolio-active-portfolio';

/** Where the portfolio list currently stands, so the switcher/pages can react. */
export type ActivePortfolioStatus = 'loading' | 'ready' | 'empty' | 'error';

/**
 * Single source of truth for the "active portfolio". The whole app is portfolio-scoped, so
 * pages read `activeId()`/`active()` here and the topbar switcher drives selection.
 *
 * The chosen portfolio is persisted to localStorage; on load we honour it if it still exists,
 * otherwise fall back to the first portfolio.
 */
@Service()
export class ActivePortfolioService {
  private readonly api = inject(PortfolioApiService);
  private readonly document = inject(DOCUMENT);

  private readonly _portfolios = signal<PortfolioSummary[]>([]);
  private readonly _activeId = signal<string | null>(this.readStoredId());
  private readonly _status = signal<ActivePortfolioStatus>('loading');

  /** All portfolios known to the app. */
  readonly portfolios = this._portfolios.asReadonly();
  /** The id of the active portfolio, or null when none is selected/available. */
  readonly activeId = this._activeId.asReadonly();
  readonly status = this._status.asReadonly();

  /** The active portfolio object, resolved against the loaded list. */
  readonly active = computed<PortfolioSummary | null>(() => {
    const id = this._activeId();
    return this._portfolios().find((p) => p.portfolioId === id) ?? null;
  });

  constructor() {
    // Keep the stored selection in sync with the signal.
    effect(() => this.writeStoredId(this._activeId()));
    this.refresh();
  }

  /** (Re)load the portfolio list, reconciling the active selection with what exists. */
  refresh(): void {
    this._status.set('loading');
    this.api
      .getPortfolios()
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(),
      )
      .subscribe((portfolios) => {
        if (portfolios === null) {
          this._status.set('error');
          return;
        }
        this._portfolios.set(portfolios);
        this.reconcileSelection(portfolios);
        this._status.set(portfolios.length === 0 ? 'empty' : 'ready');
      });
  }

  /** Set the active portfolio by id (no-op if unknown). */
  select(portfolioId: string): void {
    if (this._portfolios().some((p) => p.portfolioId === portfolioId)) {
      this._activeId.set(portfolioId);
    }
  }

  /**
   * Adopt a freshly-created portfolio: add it to the list and make it active. Callers use this
   * after createPortfolio() so the UI reflects the new portfolio without a full refresh.
   */
  adopt(portfolio: PortfolioSummary): void {
    this._portfolios.update((list) =>
      list.some((p) => p.portfolioId === portfolio.portfolioId)
        ? list
        : [...list, portfolio],
    );
    this._activeId.set(portfolio.portfolioId);
    this._status.set('ready');
  }

  /** Ensure the active id points at an existing portfolio, defaulting to the first. */
  private reconcileSelection(portfolios: PortfolioSummary[]): void {
    if (portfolios.length === 0) {
      this._activeId.set(null);
      return;
    }
    const current = this._activeId();
    const stillExists = current && portfolios.some((p) => p.portfolioId === current);
    if (!stillExists) {
      this._activeId.set(portfolios[0].portfolioId);
    }
  }

  private readStoredId(): string | null {
    try {
      return this.document.defaultView?.localStorage.getItem(STORAGE_KEY) ?? null;
    } catch {
      return null;
    }
  }

  private writeStoredId(id: string | null): void {
    try {
      const storage = this.document.defaultView?.localStorage;
      if (!storage) {
        return;
      }
      if (id) {
        storage.setItem(STORAGE_KEY, id);
      } else {
        storage.removeItem(STORAGE_KEY);
      }
    } catch {
      /* storage unavailable (private mode / SSR) — non-fatal */
    }
  }
}
