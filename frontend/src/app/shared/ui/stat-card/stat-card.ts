import { Component, input } from '@angular/core';

export type StatTrend = 'up' | 'down' | 'neutral';

/**
 * Presentational summary card: a label, a big value, an optional delta/trend line,
 * and an optional "incomplete data" hint (driven by PerformanceBalance.isComplete).
 */
@Component({
  selector: 'app-stat-card',
  templateUrl: './stat-card.html',
  styleUrl: './stat-card.scss',
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly delta = input<string | null>(null);
  readonly trend = input<StatTrend>('neutral');
  /** When true, shows a subtle hint that the underlying figure is missing snapshot coverage. */
  readonly incomplete = input(false);
}
