import { TestBed } from '@angular/core/testing';

import { CompletenessBadge } from './completeness-badge';

function render(inputs: { complete: boolean; missing?: number; asOf?: string | null }) {
  const fixture = TestBed.createComponent(CompletenessBadge);
  fixture.componentRef.setInput('complete', inputs.complete);
  if (inputs.missing !== undefined) {
    fixture.componentRef.setInput('missing', inputs.missing);
  }
  if (inputs.asOf !== undefined) {
    fixture.componentRef.setInput('asOf', inputs.asOf);
  }
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('CompletenessBadge', () => {
  it('shows "Known" with an as-of tooltip when complete', () => {
    const el = render({ complete: true, asOf: '2026-01-31' });
    const badge = el.querySelector('.badge')!;
    expect(badge.textContent?.trim()).toBe('Known');
    expect(badge.classList.contains('badge--estimate')).toBe(false);
    expect(badge.getAttribute('title')).toContain('2026-01-31');
  });

  it('shows "Estimate" with a pluralized missing count when incomplete', () => {
    const el = render({ complete: false, missing: 3 });
    const badge = el.querySelector('.badge')!;
    expect(badge.textContent?.trim()).toBe('Estimate');
    expect(badge.classList.contains('badge--estimate')).toBe(true);
    expect(badge.getAttribute('title')).toContain('3 holdings are missing');
  });

  it('uses singular wording for a single missing holding', () => {
    const el = render({ complete: false, missing: 1 });
    expect(el.querySelector('.badge')!.getAttribute('title')).toContain(
      '1 holding is missing',
    );
  });
});
