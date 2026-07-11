import { Component, computed, input } from '@angular/core';

/**
 * Renders the performance API's balance-completeness diagnostics as a small chip:
 * "Known" when every relevant holding has snapshot coverage, otherwise "Estimate"
 * with a tooltip explaining how many holdings are missing a snapshot.
 *
 * Feed it straight from a PerformanceBalance (`isComplete`, `holdingsMissingSnapshot`,
 * `snapshotAsOf`).
 */
@Component({
  selector: 'app-completeness-badge',
  template: `
    <span
      class="badge"
      [class.badge--estimate]="!complete()"
      [title]="tooltip()"
    >
      {{ complete() ? 'Known' : 'Estimate' }}
    </span>
  `,
  styleUrl: './completeness-badge.scss',
})
export class CompletenessBadge {
  readonly complete = input.required<boolean>();
  readonly missing = input(0);
  readonly asOf = input<string | null>(null);

  protected readonly tooltip = computed(() => {
    if (this.complete()) {
      const asOf = this.asOf();
      return asOf ? `Valued from snapshots as of ${asOf}.` : 'All holdings have snapshot coverage.';
    }
    const missing = this.missing();
    const holdings = missing === 1 ? 'holding is' : 'holdings are';
    return `Estimate — ${missing} ${holdings} missing a snapshot on this date.`;
  });
}
