import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { Chart, ChartDataset } from 'chart.js/auto';

import { ThemeService } from '../../../core/theme/theme.service';
import { formatCurrency } from '../../util/performance-format';

/** One plotted series. `colorVar` is a CSS custom property name so the chart follows the theme. */
export interface PerfDataset {
  label: string;
  /** null leaves a gap (e.g. a date where the balance couldn't be valued). */
  data: (number | null)[];
  kind: 'line' | 'bar';
  /** e.g. '--color-primary'. Resolved against the host element's computed styles. */
  colorVar: string;
}

/**
 * How y-axis ticks and tooltip values are shown. `percent` expects values already in percent
 * (11.4 -> "+11.4%") and emphasizes the zero line, so gains and losses read at a glance.
 */
export type PerfValueFormat = 'number' | 'currency' | 'percent';

/**
 * Thin, swappable wrapper around Chart.js. All Chart.js usage is encapsulated here, so
 * replacing the charting library later touches only this component. Colors are pulled from
 * the semantic theme tokens and the chart is rebuilt when the theme changes.
 */
@Component({
  selector: 'app-perf-chart',
  template: '<canvas #canvas [attr.aria-label]="ariaLabel()" role="img"></canvas>',
  styleUrl: './perf-chart.scss',
})
export class PerfChart {
  readonly labels = input<string[]>([]);
  readonly datasets = input<PerfDataset[]>([]);
  readonly ariaLabel = input('Performance chart');
  readonly valueFormat = input<PerfValueFormat>('number');
  /** ISO code used when `valueFormat` is `currency`. */
  readonly currencyCode = input('USD');

  private readonly canvas =
    viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly theme = inject(ThemeService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private chart?: Chart;

  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => this.chart?.destroy());

    // Build once the canvas exists, then rebuild whenever data or theme changes.
    afterNextRender(() => this.render());
    effect(() => {
      // Track inputs + theme so the effect re-runs on any change.
      this.labels();
      this.datasets();
      this.valueFormat();
      this.currencyCode();
      this.theme.theme();
      if (this.chart) {
        this.render();
      }
    });
  }

  private cssVar(name: string): string {
    return getComputedStyle(this.host.nativeElement).getPropertyValue(name).trim();
  }

  private formatValue(value: number): string {
    switch (this.valueFormat()) {
      case 'percent':
        return `${value > 0 ? '+' : ''}${value.toFixed(1)}%`;
      case 'currency':
        return formatCurrency(value, this.currencyCode());
      default:
        return new Intl.NumberFormat('en-US').format(value);
    }
  }

  private render(): void {
    this.chart?.destroy();

    const gridColor = this.cssVar('--color-border');
    const textColor = this.cssVar('--color-text-muted');
    const zeroLineColor = this.valueFormat() === 'percent' ? textColor : gridColor;

    const chartDatasets: ChartDataset[] = this.datasets().map((d) => {
      const color = this.cssVar(d.colorVar);
      if (d.kind === 'line') {
        return {
          type: 'line',
          label: d.label,
          data: d.data,
          borderColor: color,
          backgroundColor: `color-mix(in oklch, ${color} 18%, transparent)`,
          fill: true,
          tension: 0.35,
          pointRadius: 0,
          borderWidth: 2,
          order: 0,
        };
      }
      return {
        type: 'bar',
        label: d.label,
        data: d.data,
        backgroundColor: color,
        borderRadius: 4,
        maxBarThickness: 22,
        order: 1,
      };
    });

    this.chart = new Chart(this.canvas().nativeElement, {
      type: 'bar',
      data: { labels: this.labels(), datasets: chartDatasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { labels: { color: textColor, usePointStyle: true } },
          tooltip: {
            callbacks: {
              label: (item) =>
                item.parsed.y === null
                  ? `${item.dataset.label}: —`
                  : `${item.dataset.label}: ${this.formatValue(item.parsed.y)}`,
            },
          },
        },
        scales: {
          x: { grid: { display: false }, ticks: { color: textColor } },
          y: {
            grid: { color: (ctx) => (ctx.tick?.value === 0 ? zeroLineColor : gridColor) },
            ticks: { color: textColor, callback: (value) => this.formatValue(Number(value)) },
            beginAtZero: false,
          },
        },
      },
    });
  }
}
