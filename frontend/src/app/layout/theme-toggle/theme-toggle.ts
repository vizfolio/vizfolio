import { Component, computed, inject } from '@angular/core';

import { ThemeService } from '../../core/theme/theme.service';

/** Accessible button that flips the app between light and dark themes. */
@Component({
  selector: 'app-theme-toggle',
  template: `
    <button
      type="button"
      class="theme-toggle"
      [attr.aria-pressed]="isDark()"
      [attr.aria-label]="label()"
      [title]="label()"
      (click)="theme.toggle()"
    >
      @if (isDark()) {
        <!-- sun icon: click to go light -->
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" fill="currentColor">
          <path
            d="M12 4V2m0 20v-2m8-8h2M2 12h2m13.66 6.66 1.42 1.42M4.92 4.92l1.42 1.42m0 11.32-1.42 1.42M19.08 4.92l-1.42 1.42M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10Z"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            stroke-linecap="round"
          />
        </svg>
      } @else {
        <!-- moon icon: click to go dark -->
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" fill="currentColor">
          <path d="M21 12.79A9 9 0 1 1 11.21 3a7 7 0 0 0 9.79 9.79Z" />
        </svg>
      }
    </button>
  `,
  styleUrl: './theme-toggle.scss',
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);
  protected readonly isDark = computed(() => this.theme.theme() === 'dark');
  protected readonly label = computed(() =>
    this.isDark() ? 'Switch to light theme' : 'Switch to dark theme',
  );
}
