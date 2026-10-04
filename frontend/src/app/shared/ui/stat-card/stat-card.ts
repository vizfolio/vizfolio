import { Component, input } from '@angular/core';

export type StatTrend = 'up' | 'down' | 'neutral';

/**
 * Presentational summary card: a label, a big value, an optional delta/trend line and an optional
 * explanation of what the figure means. A featured card is the page's headline figure. Status can be
 * projected into the header with a `statBadge` attribute (e.g. a completeness badge).
 */
@Component({
  selector: 'app-stat-card',
  templateUrl: './stat-card.html',
  styleUrl: './stat-card.scss',
  host: { '[class.featured]': 'featured()' },
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly delta = input<string | null>(null);
  readonly trend = input<StatTrend>('neutral');
  /** A short plain-language explanation of what the figure measures. */
  readonly hint = input<string | null>(null);
  /** Emphasises the card as the headline figure. */
  readonly featured = input(false);
}
