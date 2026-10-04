import { TestBed } from '@angular/core/testing';

import { StatCard } from './stat-card';

describe('StatCard', () => {
  it('renders the label and value', async () => {
    const fixture = TestBed.createComponent(StatCard);
    fixture.componentRef.setInput('label', 'Portfolio value');
    fixture.componentRef.setInput('value', '$1,234');
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.stat-label')?.textContent).toContain('Portfolio value');
    expect(el.querySelector('.stat-value')?.textContent).toContain('$1,234');
  });

  it('shows the incomplete flag only when incomplete is true', async () => {
    const fixture = TestBed.createComponent(StatCard);
    fixture.componentRef.setInput('label', 'Value');
    fixture.componentRef.setInput('value', '$0');
    fixture.componentRef.setInput('incomplete', true);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.stat-flag')).toBeTruthy();
  });

  it('explains the figure when given a hint, and marks a featured card', async () => {
    const fixture = TestBed.createComponent(StatCard);
    fixture.componentRef.setInput('label', 'Your return');
    fixture.componentRef.setInput('value', '+12.0%');
    fixture.componentRef.setInput('hint', 'How your money did.');
    fixture.componentRef.setInput('featured', true);
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.stat-hint')?.textContent).toContain('How your money did.');
    expect(el.classList).toContain('featured');
  });

  it('shows no hint and no emphasis by default', async () => {
    const fixture = TestBed.createComponent(StatCard);
    fixture.componentRef.setInput('label', 'Value');
    fixture.componentRef.setInput('value', '$0');
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.stat-hint')).toBeNull();
    expect(el.classList).not.toContain('featured');
  });
});
