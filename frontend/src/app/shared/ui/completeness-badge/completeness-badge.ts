import { Component, computed, input } from '@angular/core';

import { PerformanceMissing } from '../../../core/api/models/performance.models';
import { missingText } from '../../util/reason-text';

/**
 * Renders the performance API's balance-completeness diagnostics as a small chip:
 * "Known" when every holding is valued, otherwise "Estimate" with a tooltip naming what couldn't be valued and why
 * (from `missingDetails`) — or "Updating…" when the only reason is that prices are still downloading (`pending`).
 *
 * Feed it straight from a PerformanceBalance (`isComplete`, `holdingsMissingSnapshot`, `missing`).
 */
@Component({
  selector: 'app-completeness-badge',
  template: `
    <span
      class="badge"
      [class.badge--estimate]="!complete() && !pending()"
      [class.badge--pending]="!complete() && pending()"
      [title]="tooltip()"
    >
      {{ label() }}
    </span>
  `,
  styleUrl: './completeness-badge.scss',
})
export class CompletenessBadge {
  readonly complete = input.required<boolean>();
  readonly missing = input(0);
  /** What couldn't be valued and why (PerformanceBalance.missing). */
  readonly missingDetails = input<readonly PerformanceMissing[]>([]);
  /** Incomplete only because prices are still downloading. */
  readonly pending = input(false);

  protected readonly label = computed(() =>
    this.complete() ? 'Known' : this.pending() ? 'Updating…' : 'Estimate',
  );

  protected readonly tooltip = computed(() => {
    if (this.complete()) {
      return 'Every holding is valued on this date.';
    }
    if (this.pending()) {
      return 'Prices are still downloading; this updates on its own in a moment.';
    }
    const named = missingText(this.missingDetails());
    if (named) {
      return `Estimate — ${named}`;
    }
    const missing = this.missing();
    const holdings = missing === 1 ? 'holding' : 'holdings';
    return `Estimate — ${missing} ${holdings} couldn't be valued on this date.`;
  });
}
