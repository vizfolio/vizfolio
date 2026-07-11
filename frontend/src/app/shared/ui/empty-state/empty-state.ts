import { Component, input } from '@angular/core';

/**
 * Presentational empty/first-run state: an optional icon, a heading, a message, and an
 * optional action slot (project a button/link via content). Used when a page has no data
 * yet (no portfolios, no accounts, empty ledger, ...).
 */
@Component({
  selector: 'app-empty-state',
  template: `
    <div class="empty-state">
      @if (iconPath()) {
        <svg class="icon" viewBox="0 0 24 24" width="40" height="40" aria-hidden="true" fill="currentColor">
          <path [attr.d]="iconPath()" />
        </svg>
      }
      <h2 class="title">{{ title() }}</h2>
      @if (message()) {
        <p class="message">{{ message() }}</p>
      }
      <div class="actions">
        <ng-content />
      </div>
    </div>
  `,
  styleUrl: './empty-state.scss',
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly message = input<string | null>(null);
  /** Inline SVG path data (24x24 viewBox) for an optional illustrative icon. */
  readonly iconPath = input<string | null>(null);
}
