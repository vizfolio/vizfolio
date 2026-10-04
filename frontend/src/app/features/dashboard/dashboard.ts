import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { AccountSummary, PortfolioPerformance } from '../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PerfChart, PerfDataset } from '../../shared/ui/perf-chart/perf-chart';
import { ReturnsChart } from '../../shared/ui/returns-chart/returns-chart';
import { PerformanceHeadline } from '../../shared/ui/performance-headline/performance-headline';
import { ImportDropZone } from '../accounts/import-drop-zone/import-drop-zone';
import { ImportOnboarding } from '../accounts/import-onboarding/import-onboarding';
import { SAMPLE_PERFORMANCE, buildValueSeries } from './dashboard.util';
import { pollWhilePending } from '../../shared/util/poll';
import { performanceAwaitsPrices } from '../../shared/util/performance-format';

/**
 * loading — resolving portfolios or performance
 * ready   — showing real performance for the active portfolio
 * empty   — no portfolios exist yet (first run)
 * error   — API unreachable; showing sample data so the shell stays legible
 */
type DashboardStatus = 'loading' | 'ready' | 'empty' | 'error';

type PerfFetchStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Landing page: the shared headline row + value and returns charts for the active portfolio. */
@Component({
  selector: 'app-dashboard',
  imports: [EmptyState, ImportDropZone, ImportOnboarding, PerfChart, PerformanceHeadline, ReturnsChart, RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);

  private readonly performance = signal<PortfolioPerformance | null>(null);
  private readonly perfStatus = signal<PerfFetchStatus>('idle');
  /** Bumped after an import from the onboarding drop zone, to reload what's shown. */
  private readonly reloads = signal(0);

  protected readonly portfolioId = this.activePortfolio.activeId;
  /** The active portfolio's accounts (null until loaded): none yet means first run, so the drop zone shows. */
  protected readonly accounts = signal<AccountSummary[] | null>(null);
  protected readonly needsFirstImport = computed(() => this.accounts()?.length === 0);

  protected readonly portfolioName = computed(
    () => this.activePortfolio.active()?.name ?? null,
  );

  /** Overall page state, combining portfolio-list status with the performance fetch. */
  protected readonly status = computed<DashboardStatus>(() => {
    const listStatus = this.activePortfolio.status();
    if (listStatus === 'loading') {
      return 'loading';
    }
    if (listStatus === 'empty') {
      return 'empty';
    }
    if (listStatus === 'error') {
      return 'error';
    }
    // Portfolios are loaded; defer to the performance fetch.
    switch (this.perfStatus()) {
      case 'ready':
        return 'ready';
      case 'error':
        return 'error';
      default:
        return 'loading';
    }
  });

  /** What the cards/chart render: real data when ready, sample data on error. */
  protected readonly displayPerformance = computed<PortfolioPerformance | null>(() => {
    switch (this.status()) {
      case 'ready':
        return this.performance();
      case 'error':
        return SAMPLE_PERFORMANCE;
      default:
        return null;
    }
  });

  /** Banner shown when we're not displaying the active portfolio's real data. */
  protected readonly notice = computed(() =>
    this.status() === 'error'
      ? "Couldn't reach the API — showing sample data instead."
      : null,
  );

  private readonly series = computed(() => {
    const p = this.displayPerformance();
    return p ? buildValueSeries(p) : null;
  });

  protected readonly chartLabels = computed(() => this.series()?.labels ?? []);
  protected readonly chartNote = computed(() => this.series()?.note ?? '');

  protected readonly chartDatasets = computed<PerfDataset[]>(() => {
    const series = this.series();
    if (!series) {
      return [];
    }
    return [
      { label: 'Portfolio value', data: series.value, kind: 'line', colorVar: '--color-primary' },
      { label: 'Deposits', data: series.deposits, kind: 'bar', colorVar: '--color-accent' },
      { label: 'Withdrawals', data: series.withdrawals, kind: 'bar', colorVar: '--color-negative' },
    ];
  });

  constructor() {
    const request = computed(() => ({ portfolioId: this.activePortfolio.activeId(), n: this.reloads() }));

    toObservable(request)
      .pipe(
        switchMap(({ portfolioId }) =>
          portfolioId ? this.api.getAccounts(portfolioId).pipe(catchError(() => of(null))) : of(null),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((accounts) => this.accounts.set(accounts));

    // Re-fetch performance whenever the active portfolio changes (e.g. via the switcher).
    toObservable(request)
      .pipe(
        switchMap(({ portfolioId }) => {
          if (!portfolioId) {
            this.perfStatus.set('idle');
            return of<PortfolioPerformance | null>(null);
          }
          this.perfStatus.set('loading');
          return pollWhilePending(() => this.api.getPerformance(portfolioId), performanceAwaitsPrices).pipe(
            catchError(() => {
              this.perfStatus.set('error');
              return of<PortfolioPerformance | null>(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((perf) => {
        this.performance.set(perf);
        if (perf) {
          this.perfStatus.set('ready');
        }
      });
  }

  /** The first files were imported: show the portfolio. */
  protected onFirstImport(): void {
    this.reloads.update((n) => n + 1);
  }
}
