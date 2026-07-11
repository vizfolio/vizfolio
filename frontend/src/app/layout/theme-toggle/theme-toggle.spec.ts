import { TestBed } from '@angular/core/testing';

import { ThemeService } from '../../core/theme/theme.service';
import { ThemeToggle } from './theme-toggle';

describe('ThemeToggle', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  it('renders an accessible toggle button reflecting the current theme', async () => {
    const fixture = TestBed.createComponent(ThemeToggle);
    await fixture.whenStable();

    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    expect(button.getAttribute('aria-label')).toContain('dark');
    expect(button.getAttribute('aria-pressed')).toBe('false');
  });

  it('toggles the theme when clicked', async () => {
    const fixture = TestBed.createComponent(ThemeToggle);
    await fixture.whenStable();
    const theme = TestBed.inject(ThemeService);

    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    button.click();
    await fixture.whenStable();

    expect(theme.theme()).toBe('dark');
    expect(button.getAttribute('aria-pressed')).toBe('true');
  });
});
