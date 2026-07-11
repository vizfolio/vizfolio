import { DOCUMENT } from '@angular/common';
import { Service, effect, inject, signal } from '@angular/core';

/** Supported theme names. Widen this union (and add a token block in styles.scss) to add themes. */
export type ThemeName = 'light' | 'dark';

const STORAGE_KEY = 'vizfolio-theme';

/**
 * Owns the active UI theme. The theme is a signal so components react to it; an effect
 * reflects it onto `<html data-theme>` (which drives the semantic CSS tokens) and
 * persists the user's choice to localStorage.
 */
@Service()
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly theme = signal<ThemeName>(this.resolveInitialTheme());

  constructor() {
    effect(() => {
      const theme = this.theme();
      this.document.documentElement.dataset['theme'] = theme;
      this.safeStore(theme);
    });
  }

  /** Flip between light and dark. */
  toggle(): void {
    this.theme.update((current) => (current === 'dark' ? 'light' : 'dark'));
  }

  set(theme: ThemeName): void {
    this.theme.set(theme);
  }

  /** Stored preference wins; otherwise honour the OS `prefers-color-scheme`. */
  private resolveInitialTheme(): ThemeName {
    const stored = this.safeRead();
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }
    const prefersDark =
      this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)')
        .matches ?? false;
    return prefersDark ? 'dark' : 'light';
  }

  private safeRead(): string | null {
    try {
      return this.document.defaultView?.localStorage.getItem(STORAGE_KEY) ?? null;
    } catch {
      return null;
    }
  }

  private safeStore(theme: ThemeName): void {
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      /* storage unavailable (private mode / SSR) — non-fatal */
    }
  }
}
