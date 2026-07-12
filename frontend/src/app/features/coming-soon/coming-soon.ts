import { Component, input } from '@angular/core';

/** Generic placeholder page for routes that aren't built yet. `title` is bound from route data. */
@Component({
  selector: 'app-coming-soon',
  template: `
    <section class="coming-soon">
      <h1>{{ title() }}</h1>
      <p>This section is coming soon.</p>
    </section>
  `,
  styles: `
    .coming-soon {
      max-width: 640px;
      margin: 0 auto;
      padding: var(--space-6);
      text-align: center;
      color: var(--color-text-muted);
    }
    .coming-soon h1 {
      color: var(--color-text);
      margin-bottom: var(--space-3);
    }
  `,
})
export class ComingSoon {
  readonly title = input('Coming soon');
}
