import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';

import { PortfolioApiService } from '../../../core/api/portfolio-api.service';
import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { CompletenessBadge } from '../../../shared/ui/completeness-badge/completeness-badge';
import { PerfChart, PerfDataset } from '../../../shared/ui/perf-chart/perf-chart';
import { StatCard } from '../../../shared/ui/stat-card/stat-card';
import {
  buildSyntheticSeries,
  formatCurrency,
  formatPercent,
  trendOf,
} from '../../dashboard/dashboard.util';

type Status = 'loading' | 'ready' | 'error';

/** Performance tab: account-scoped returns over an optional date range. */
@Component({
  selector: 'app-account-performance',
  imports: [StatCard, CompletenessBadge, PerfChart],
  templateUrl: './account-performance.html',
  styleUrl: './account-performance.scss',
})
export class AccountPerformance {
  readonly portfolioId = input.required<string>();
  readonly accountId = input.required<string>();

  private readonly api = inject(PortfolioApiService);

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly status = signal<Status>('loading');
  protected readonly performance = signal<PortfolioPerformance | null>(null);

  private readonly query = computed(() => ({
    portfolioId: this.portfolioId(),
    accountId: this.accountId(),
    from: this.from(),
    to: this.to(),
  }));

  constructor() {
    toObservable(this.query)
      .pipe(
        switchMap(({ portfolioId, accountId, from, to }) => {
          this.status.set('loading');
          return this.api
            .getAccountPerformance(portfolioId, accountId, from || undefined, to || undefined)
            .pipe(
              catchError(() => {
                this.status.set('error');
                return of<PortfolioPerformance | null>(null);
              }),
            );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((perf) => {
        this.performance.set(perf);
        if (perf) {
          this.status.set('ready');
        }
      });
  }

  protected onFrom(event: Event): void {
    this.from.set((event.target as HTMLInputElement).value);
  }

  protected onTo(event: Event): void {
    this.to.set((event.target as HTMLInputElement).value);
  }

  protected readonly currency = computed(() => this.performance()?.currencyCode ?? 'USD');

  protected money(value: number | undefined): string {
    return value === undefined ? '—' : formatCurrency(value, this.currency());
  }

  protected readonly netTrend = computed(() =>
    trendOf(this.performance()?.contributions.net ?? null),
  );

  protected readonly twrLabel = computed(() =>
    formatPercent(this.performance()?.returns.timeWeighted.rate ?? null),
  );
  protected readonly twrTrend = computed(() =>
    trendOf(this.performance()?.returns.timeWeighted.rate ?? null),
  );
  protected readonly mwrLabel = computed(() =>
    formatPercent(this.performance()?.returns.moneyWeighted.rate ?? null),
  );
  protected readonly mwrTrend = computed(() =>
    trendOf(this.performance()?.returns.moneyWeighted.rate ?? null),
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
      { label: 'Value', data: series.value, kind: 'line', colorVar: '--color-primary' },
      { label: 'Deposits', data: series.deposits, kind: 'bar', colorVar: '--color-accent' },
      { label: 'Withdrawals', data: series.withdrawals, kind: 'bar', colorVar: '--color-negative' },
    ];
  });
}
