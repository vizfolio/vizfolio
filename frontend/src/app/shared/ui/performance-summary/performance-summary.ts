import { Component, computed, input } from '@angular/core';

import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import {
  buildSyntheticSeries,
  formatCurrency,
  formatPercent,
  trendOf,
} from '../../util/performance-format';
import { CompletenessBadge } from '../completeness-badge/completeness-badge';
import { PerfChart, PerfDataset } from '../perf-chart/perf-chart';
import { StatCard } from '../stat-card/stat-card';

/**
 * Presentational summary of a {@link PortfolioPerformance}: headline stat cards, a balances /
 * contributions / returns breakdown (with completeness badges and null-return reasons), and the
 * synthetic value chart. Shared by the dashboard-style views (portfolio and account).
 */
@Component({
  selector: 'app-performance-summary',
  imports: [StatCard, CompletenessBadge, PerfChart],
  templateUrl: './performance-summary.html',
  styleUrl: './performance-summary.scss',
})
export class PerformanceSummary {
  readonly performance = input.required<PortfolioPerformance>();

  protected readonly currency = computed(() => this.performance().currencyCode || 'USD');

  protected money(value: number): string {
    return formatCurrency(value, this.currency());
  }

  protected readonly netTrend = computed(() =>
    trendOf(this.performance().contributions.net),
  );

  protected readonly twrLabel = computed(() =>
    formatPercent(this.performance().returns.timeWeighted.rate),
  );
  protected readonly twrTrend = computed(() =>
    trendOf(this.performance().returns.timeWeighted.rate),
  );
  protected readonly mwrLabel = computed(() =>
    formatPercent(this.performance().returns.moneyWeighted.rate),
  );
  protected readonly mwrTrend = computed(() =>
    trendOf(this.performance().returns.moneyWeighted.rate),
  );

  protected readonly chartLabels = computed(() =>
    buildSyntheticSeries(this.performance()).labels,
  );

  protected readonly chartDatasets = computed<PerfDataset[]>(() => {
    const series = buildSyntheticSeries(this.performance());
    return [
      { label: 'Value', data: series.value, kind: 'line', colorVar: '--color-primary' },
      { label: 'Deposits', data: series.deposits, kind: 'bar', colorVar: '--color-accent' },
      { label: 'Withdrawals', data: series.withdrawals, kind: 'bar', colorVar: '--color-negative' },
    ];
  });
}
