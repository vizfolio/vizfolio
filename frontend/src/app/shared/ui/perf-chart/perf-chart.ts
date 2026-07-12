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

/** One plotted series. `colorVar` is a CSS custom property name so the chart follows the theme. */
export interface PerfDataset {
  label: string;
  data: number[];
  kind: 'line' | 'bar';
  /** e.g. '--color-primary'. Resolved against the host element's computed styles. */
  colorVar: string;
}

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
      this.theme.theme();
      if (this.chart) {
        this.render();
      }
    });
  }

  private cssVar(name: string): string {
    return getComputedStyle(this.host.nativeElement).getPropertyValue(name).trim();
  }

  private render(): void {
    this.chart?.destroy();

    const gridColor = this.cssVar('--color-border');
    const textColor = this.cssVar('--color-text-muted');

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
        },
        scales: {
          x: { grid: { display: false }, ticks: { color: textColor } },
          y: {
            grid: { color: gridColor },
            ticks: { color: textColor },
            beginAtZero: false,
          },
        },
      },
    });
  }
}
