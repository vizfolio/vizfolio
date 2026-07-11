import { Component, output } from '@angular/core';

import { ThemeToggle } from '../theme-toggle/theme-toggle';

/** Slim top bar: menu toggle (narrow screens), brand, theme toggle, account slot. */
@Component({
  selector: 'app-topbar',
  imports: [ThemeToggle],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  /** Emitted when the user taps the hamburger to open/close the sidebar on narrow screens. */
  readonly menuToggle = output<void>();
}
