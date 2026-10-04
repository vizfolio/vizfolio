import { Component, computed, input, signal } from '@angular/core';

import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { buildReturnSeries } from '../../util/performance-format';
import { INVESTMENT_RETURN_LABEL, YOUR_RETURN_LABEL } from '../../util/reason-text';
import { PerfChart, PerfDataset, PerfValueFormat } from '../perf-chart/perf-chart';

/** `percent` = cumulative return; `gain` = cumulative investment gain in currency. */
export type ReturnsMode = 'percent' | 'gain';

/**
 * "Investment returns over time": how the investments themselves performed, with contributions
 * stripped out. Toggles between the cumulative time-weighted return (%, ending at the "Investment
 * return" figure — not the money-weighted "Your return" headline) and the cumulative investment gain
 * (value − starting balance − net contributions).
 */
@Component({
  selector: 'app-returns-chart',
  imports: [PerfChart],
  templateUrl: './returns-chart.html',
  styleUrl: './returns-chart.scss',
})
export class ReturnsChart {
  readonly performance = input.required<PortfolioPerformance>();
  /** Heading level so the card fits the host page's outline. */
  readonly headingLevel = input<2 | 3>(3);

  protected readonly mode = signal<ReturnsMode>('percent');

  private readonly series = computed(() => buildReturnSeries(this.performance()));

  protected readonly labels = computed(() => this.series().labels);
  protected readonly note = computed(() => this.series().note);
  protected readonly currency = computed(() => this.performance().currencyCode || 'USD');

  protected readonly valueFormat = computed<PerfValueFormat>(() =>
    this.mode() === 'percent' ? 'percent' : 'currency',
  );

  protected readonly datasets = computed<PerfDataset[]>(() => {
    const series = this.series();
    return this.mode() === 'percent'
      ? [{ label: 'Cumulative investment return', data: series.returnPct, kind: 'line', colorVar: '--color-primary' }]
      : [{ label: 'Investment gain', data: series.gain, kind: 'line', colorVar: '--color-primary' }];
  });

  /** What the line measures, and that it can differ from the headline money-weighted figure. */
  protected readonly caption = computed(() => {
    if (this.mode() === 'gain') {
      return 'Value change not explained by deposits and withdrawals.';
    }
    const approximate = this.performance().returns.timeWeighted.fallbackReason
      ? ' Approximate: some days couldn’t be valued.'
      : '';
    return `Time-weighted: ends at the ${INVESTMENT_RETURN_LABEL.toLowerCase()}, not ${YOUR_RETURN_LABEL.toLowerCase()}.${approximate}`;
  });

  protected readonly ariaLabel = computed(() =>
    this.mode() === 'percent'
      ? 'Cumulative time-weighted return over time'
      : 'Cumulative investment gain over time, excluding deposits and withdrawals',
  );
}
