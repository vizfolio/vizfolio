import { Component, computed, input } from '@angular/core';

import { ImportWarning } from '../../../core/api/models/imports.models';

/**
 * What an import didn't fully understand (unmapped labels, unknown OFX aggregates, skipped rows…), each with how
 * many rows it affected and a few examples. Collapsed behind a native <details> so a long list doesn't push the
 * result away; nothing renders when there are no warnings.
 */
@Component({
  selector: 'app-import-warnings',
  template: `
    @if (warnings().length > 0) {
      <details class="warnings">
        <summary>{{ title() }}</summary>
        <ul>
          @for (warning of warnings(); track warning.code + warning.message) {
            <li>
              {{ warning.message }}
              @if (warning.count > 1) {
                <span class="count">({{ warning.count }} rows)</span>
              }
              @if (warning.samples.length > 0) {
                <span class="samples">e.g. {{ warning.samples.join(', ') }}</span>
              }
            </li>
          }
        </ul>
      </details>
    }
  `,
  styles: `
    .warnings {
      margin-top: var(--space-2);
      font-size: 0.85rem;
    }
    summary {
      cursor: pointer;
      color: var(--color-text);
      font-weight: 600;
    }
    ul {
      margin: var(--space-2) 0 0;
      padding-left: var(--space-5);
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      color: var(--color-text-muted);
    }
    .count,
    .samples {
      margin-left: var(--space-1);
    }
    .samples {
      font-family: var(--font-mono, monospace);
      font-size: 0.8rem;
    }
  `,
})
export class ImportWarnings {
  readonly warnings = input.required<readonly ImportWarning[]>();

  protected readonly title = computed(() => {
    const rows = this.warnings().reduce((sum, w) => sum + w.count, 0);
    return `${rows} ${rows === 1 ? 'row needs' : 'rows need'} a look`;
  });
}
