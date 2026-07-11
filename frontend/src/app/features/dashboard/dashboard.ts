import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../core/api/portfolio-api.service';
import { PortfolioPerformance } from '../../core/api/models/performance.models';
import { PerfChart, PerfDataset } from '../../shared/ui/perf-chart/perf-chart';
import { StatCard } from '../../shared/ui/stat-card/stat-card';
import {
  SAMPLE_PERFORMANCE,
  buildSyntheticSeries,
  formatCurrency,
  formatPercent,
  trendOf,
} from './dashboard.util';

type DashboardStatus = 'loading' | 'ready' | 'sample' | 'error';

/** Landing page: headline portfolio stats plus a performance chart. */
@Component({
  selector: 'app-dashboard',
  imports: [StatCard, PerfChart],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(PortfolioApiService);

  protected readonly status = signal<DashboardStatus>('loading');
  protected readonly portfolioName = signal<string | null>(null);
  protected readonly performance = signal<PortfolioPerformance | null>(null);

  /** A banner message when we're not showing real data, else null. */
  protected readonly notice = computed(() => {
    switch (this.status()) {
      case 'sample':
        return 'No portfolios found — showing sample data. Import a portfolio to see your real performance.';
      case 'error':
        return "Couldn't reach the API — showing sample data instead.";
      default:
        return null;
    }
  });

  protected readonly valueLabel = computed(() => {
    const p = this.performance();
    return p ? formatCurrency(p.endingBalance.value, p.currencyCode) : '—';
  });
  protected readonly valueIncomplete = computed(
    () => this.performance()?.endingBalance.isComplete === false,
  );

  protected readonly depositsLabel = computed(() => {
    const p = this.performance();
    return p ? formatCurrency(p.contributions.deposits, p.currencyCode) : '—';
  });

  protected readonly withdrawalsLabel = computed(() => {
    const p = this.performance();
    return p
      ? formatCurrency(Math.abs(p.contributions.withdrawals), p.currencyCode)
      : '—';
  });

  protected readonly netLabel = computed(() => {
    const p = this.performance();
    return p ? formatCurrency(p.contributions.net, p.currencyCode) : '—';
  });
  protected readonly netTrend = computed(() =>
    trendOf(this.performance()?.contributions.net ?? null),
  );

  protected readonly returnLabel = computed(() =>
    formatPercent(this.performance()?.returns.timeWeighted.rate ?? null),
  );
  protected readonly returnTrend = computed(() =>
    trendOf(this.performance()?.returns.timeWeighted.rate ?? null),
  );

  protected readonly chartLabels = computed(() => {
    const p = this.performance();
    return p ? buildSyntheticSeries(p).labels : [];
  });

  protected readonly chartDatasets = computed<PerfDataset[]>(() => {
    const p = this.performance();
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
    this.api
      .getPortfolios()
      .pipe(
        switchMap((portfolios) => {
          const first = portfolios[0];
          if (!first) {
            return of({ portfolio: null, perf: null });
          }
          return this.api.getPerformance(first.portfolioId).pipe(
            switchMap((perf) => of({ portfolio: first, perf })),
          );
        }),
        catchError(() => of({ portfolio: null, perf: null, failed: true as const })),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        if ('failed' in result && result.failed) {
          this.showSample('error');
          return;
        }
        if (result.perf && result.portfolio) {
          this.portfolioName.set(result.portfolio.name);
          this.performance.set(result.perf);
          this.status.set('ready');
        } else {
          this.showSample('sample');
        }
      });
  }

  /** Falls back to illustrative data so the shell stays legible with an empty DB or API error. */
  private showSample(status: 'sample' | 'error'): void {
    this.performance.set(SAMPLE_PERFORMANCE);
    this.portfolioName.set('Sample portfolio');
    this.status.set(status);
  }
}
