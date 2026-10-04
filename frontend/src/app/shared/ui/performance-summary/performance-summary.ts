import { Component, computed, input } from '@angular/core';

import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { buildValueSeries, returnFigure, trendOf } from '../../util/performance-format';
import {
  INVESTMENT_RETURN_LABEL,
  YOUR_RETURN_LABEL,
  fallbackText,
  returnMethodText,
} from '../../util/reason-text';
import { PerfChart, PerfDataset } from '../perf-chart/perf-chart';
import { PerformanceHeadline } from '../performance-headline/performance-headline';
import { ReturnsChart } from '../returns-chart/returns-chart';

/**
 * Presentational summary of a {@link PortfolioPerformance} for the portfolio and account Performance
 * views: the shared headline row (the same cards as the dashboard), a collapsed "How this is
 * calculated" panel (methods, annualized rate, fallbacks, reasons), and the value- and
 * returns-over-time charts.
 */
@Component({
  selector: 'app-performance-summary',
  imports: [PerformanceHeadline, PerfChart, ReturnsChart],
  templateUrl: './performance-summary.html',
  styleUrl: './performance-summary.scss',
})
export class PerformanceSummary {
  readonly performance = input.required<PortfolioPerformance>();
  /** Gross deposits / withdrawals under net contributions; off at portfolio scope (see PerformanceHeadline). */
  readonly showGrossFlows = input(true);
  /** Where "Review data health" goes when something couldn't be valued (see PerformanceHeadline). */
  readonly healthLink = input<string[] | null>(null);
  readonly healthQuery = input<Record<string, string> | null>(null);

  protected readonly yourReturnLabel = YOUR_RETURN_LABEL;
  protected readonly investmentReturnLabel = INVESTMENT_RETURN_LABEL;
  protected readonly methodText = returnMethodText;
  protected readonly fallbackText = fallbackText;

  protected readonly twrFigure = computed(() => {
    const p = this.performance();
    return returnFigure(p.returns.timeWeighted, p.from, p.to);
  });
  protected readonly twrTrend = computed(() =>
    trendOf(this.performance().returns.timeWeighted.rate),
  );
  protected readonly mwrFigure = computed(() => {
    const p = this.performance();
    return returnFigure(p.returns.moneyWeighted, p.from, p.to);
  });
  protected readonly mwrTrend = computed(() =>
    trendOf(this.performance().returns.moneyWeighted.rate),
  );

  private readonly series = computed(() => buildValueSeries(this.performance()));

  protected readonly chartLabels = computed(() => this.series().labels);
  protected readonly chartNote = computed(() => this.series().note);

  protected readonly chartDatasets = computed<PerfDataset[]>(() => {
    const series = this.series();
    return [
      { label: 'Value', data: series.value, kind: 'line', colorVar: '--color-primary' },
      { label: 'Deposits', data: series.deposits, kind: 'bar', colorVar: '--color-accent' },
      { label: 'Withdrawals', data: series.withdrawals, kind: 'bar', colorVar: '--color-negative' },
    ];
  });
}
