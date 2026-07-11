import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { PortfolioPerformance } from '../../core/api/models/performance.models';
import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PerfChart, PerfDataset } from '../../shared/ui/perf-chart/perf-chart';
import { StatCard } from '../../shared/ui/stat-card/stat-card';
import {
  SAMPLE_PERFORMANCE,
  buildSyntheticSeries,
  formatCurrency,
  formatPercent,
  trendOf,
} from './dashboard.util';

/**
 * loading — resolving portfolios or performance
 * ready   — showing real performance for the active portfolio
 * empty   — no portfolios exist yet (first run)
 * error   — API unreachable; showing sample data so the shell stays legible
 */
type DashboardStatus = 'loading' | 'ready' | 'empty' | 'error';

type PerfFetchStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Landing page: headline stats + performance chart for the active portfolio. */
@Component({
  selector: 'app-dashboard',
  imports: [StatCard, PerfChart, EmptyState, RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(PortfolioApiService);
  private readonly activePortfolio = inject(ActivePortfolioService);

  private readonly performance = signal<PortfolioPerformance | null>(null);
  private readonly perfStatus = signal<PerfFetchStatus>('idle');

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
  private readonly displayPerformance = computed<PortfolioPerformance | null>(() => {
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

  protected readonly valueLabel = computed(() => {
    const p = this.displayPerformance();
    return p ? formatCurrency(p.endingBalance.value, p.currencyCode) : '—';
  });
  protected readonly valueIncomplete = computed(
    () => this.displayPerformance()?.endingBalance.isComplete === false,
  );

  protected readonly depositsLabel = computed(() => {
    const p = this.displayPerformance();
    return p ? formatCurrency(p.contributions.deposits, p.currencyCode) : '—';
  });

  protected readonly withdrawalsLabel = computed(() => {
    const p = this.displayPerformance();
    return p
      ? formatCurrency(Math.abs(p.contributions.withdrawals), p.currencyCode)
      : '—';
  });

  protected readonly netLabel = computed(() => {
    const p = this.displayPerformance();
    return p ? formatCurrency(p.contributions.net, p.currencyCode) : '—';
  });
  protected readonly netTrend = computed(() =>
    trendOf(this.displayPerformance()?.contributions.net ?? null),
  );

  protected readonly returnLabel = computed(() =>
    formatPercent(this.displayPerformance()?.returns.timeWeighted.rate ?? null),
  );
  protected readonly returnTrend = computed(() =>
    trendOf(this.displayPerformance()?.returns.timeWeighted.rate ?? null),
  );

  protected readonly chartLabels = computed(() => {
    const p = this.displayPerformance();
    return p ? buildSyntheticSeries(p).labels : [];
  });

  protected readonly chartDatasets = computed<PerfDataset[]>(() => {
    const p = this.displayPerformance();
    if (!p) {
      return [];
    }
    const series = buildSyntheticSeries(p);
    return [
      { label: 'Portfolio value', data: series.value, kind: 'line', colorVar: '--color-primary' },
      { label: 'Deposits', data: series.deposits, kind: 'bar', colorVar: '--color-accent' },
      { label: 'Withdrawals', data: series.withdrawals, kind: 'bar', colorVar: '--color-negative' },
    ];
  });

  constructor() {
    // Re-fetch performance whenever the active portfolio changes (e.g. via the switcher).
    toObservable(this.activePortfolio.activeId)
      .pipe(
        switchMap((portfolioId) => {
          if (!portfolioId) {
            this.perfStatus.set('idle');
            return of<PortfolioPerformance | null>(null);
          }
          this.perfStatus.set('loading');
          return this.api.getPerformance(portfolioId).pipe(
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
}
