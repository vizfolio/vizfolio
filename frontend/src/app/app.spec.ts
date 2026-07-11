import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the shell with the Vizfolio brand and sidebar nav', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('app-shell')).toBeTruthy();
    expect(el.querySelector('.brand')?.textContent).toContain('Vizfolio');
    expect(el.querySelector('nav[aria-label="Primary"]')).toBeTruthy();
    expect(el.querySelectorAll('.nav-link').length).toBeGreaterThan(0);
  });
});
