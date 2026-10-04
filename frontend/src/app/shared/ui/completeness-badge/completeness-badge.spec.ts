import { TestBed } from '@angular/core/testing';

import { PerformanceMissing } from '../../../core/api/models/performance.models';
import { CompletenessBadge } from './completeness-badge';

function render(inputs: {
  complete: boolean;
  missing?: number;
  missingDetails?: PerformanceMissing[];
  pending?: boolean;
}) {
  const fixture = TestBed.createComponent(CompletenessBadge);
  fixture.componentRef.setInput('complete', inputs.complete);
  if (inputs.missing !== undefined) {
    fixture.componentRef.setInput('missing', inputs.missing);
  }
  if (inputs.missingDetails !== undefined) {
    fixture.componentRef.setInput('missingDetails', inputs.missingDetails);
  }
  if (inputs.pending !== undefined) {
    fixture.componentRef.setInput('pending', inputs.pending);
  }
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('CompletenessBadge', () => {
  it('shows "Known" when every holding is valued', () => {
    const el = render({ complete: true });
    const badge = el.querySelector('.badge')!;
    expect(badge.textContent?.trim()).toBe('Known');
    expect(badge.classList.contains('badge--estimate')).toBe(false);
    expect(badge.getAttribute('title')).toBe('Every holding is valued on this date.');
  });

  it('shows "Estimate" naming what could not be valued and why', () => {
    const el = render({
      complete: false,
      missing: 3,
      missingDetails: [
        { accountId: 'a1', accountHoldingId: 'h1', symbol: 'ZXFND', cause: 'NoPrice' },
        { accountId: 'a1', accountHoldingId: 'h2', symbol: 'ZXBND', cause: 'NoPrice' },
        { accountId: 'a1', accountHoldingId: null, symbol: null, cause: 'MaterialMismatch' },
      ],
    });
    const badge = el.querySelector('.badge')!;
    expect(badge.textContent?.trim()).toBe('Estimate');
    expect(badge.classList.contains('badge--estimate')).toBe(true);
    expect(badge.getAttribute('title')).toBe("Estimate — We couldn't value ZXFND: no price available (+2 more).");
  });

  it('falls back to a count, singular or plural, without details', () => {
    expect(render({ complete: false, missing: 1 }).querySelector('.badge')!.getAttribute('title')).toContain(
      "1 holding couldn't be valued",
    );
    expect(render({ complete: false, missing: 3 }).querySelector('.badge')!.getAttribute('title')).toContain(
      "3 holdings couldn't be valued",
    );
  });

  it('shows "Updating…" rather than "Estimate" while prices are still downloading', () => {
    const el = render({ complete: false, missing: 2, pending: true });
    const badge = el.querySelector('.badge')!;
    expect(badge.textContent?.trim()).toBe('Updating…');
    expect(badge.classList.contains('badge--estimate')).toBe(false);
    expect(badge.classList.contains('badge--pending')).toBe(true);
    expect(badge.getAttribute('title')).toContain('still downloading');
  });
});
