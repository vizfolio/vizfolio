import { Component, inject, output } from '@angular/core';

import { ActivePortfolioService } from '../../core/portfolio/active-portfolio.service';
import { ThemeToggle } from '../theme-toggle/theme-toggle';

/** Slim top bar: menu toggle (narrow screens), brand, portfolio switcher, theme toggle. */
@Component({
  selector: 'app-topbar',
  imports: [ThemeToggle],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  protected readonly portfolios = inject(ActivePortfolioService);

  /** Emitted when the user taps the hamburger to open/close the sidebar on narrow screens. */
  readonly menuToggle = output<void>();

  /** Reflects the `<select>` choice back into the active-portfolio state. */
  protected onSelect(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    if (id) {
      this.portfolios.select(id);
    }
  }
}
