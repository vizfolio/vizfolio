import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  function createAndFlush(): ThemeService {
    const service = TestBed.inject(ThemeService);
    // Flush the reflect-to-DOM / persistence effect.
    TestBed.inject(ApplicationRef).tick();
    return service;
  }

  it('defaults to light when nothing is stored and no dark preference', () => {
    const service = createAndFlush();
    expect(service.theme()).toBe('light');
    expect(document.documentElement.dataset['theme']).toBe('light');
  });

  it('restores a persisted theme from localStorage', () => {
    localStorage.setItem('vizfolio-theme', 'dark');
    const service = createAndFlush();
    expect(service.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('toggle() flips the theme, reflects it to <html>, and persists it', () => {
    const service = createAndFlush();

    service.toggle();
    TestBed.inject(ApplicationRef).tick();

    expect(service.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(localStorage.getItem('vizfolio-theme')).toBe('dark');
  });
});
