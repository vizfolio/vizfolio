import { TestBed } from '@angular/core/testing';

import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { SAMPLE_PERFORMANCE } from '../../../features/dashboard/dashboard.util';
import { PerformanceHeadline } from './performance-headline';

async function render(performance: PortfolioPerformance, inputs: Record<string, unknown> = {}) {
  const fixture = TestBed.createComponent(PerformanceHeadline);
  fixture.componentRef.setInput('performance', performance);
  for (const [name, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(name, value);
  }
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  return { el, cards: Array.from(el.querySelectorAll('app-stat-card')) };
}

describe('PerformanceHeadline', () => {
  it('shows the same four cards in the same order on every page', async () => {
    const { cards } = await render(SAMPLE_PERFORMANCE);

    expect(cards.map((c) => c.querySelector('.stat-label')?.textContent?.trim())).toEqual([
      'Balance',
      'Your return',
      'Investment return',
      'Net contributions',
    ]);
  });

  it('calls the balance "Portfolio value" when asked (the dashboard)', async () => {
    const { cards } = await render(SAMPLE_PERFORMANCE, { balanceLabel: 'Portfolio value' });

    expect(cards[0].querySelector('.stat-label')?.textContent).toContain('Portfolio value');
  });

  it('shows the ending balance, where the period started, and how complete the valuation is', async () => {
    const { cards } = await render(SAMPLE_PERFORMANCE);

    expect(cards[0].querySelector('.stat-value')?.textContent).toContain('$58,750');
    expect(cards[0].querySelector('.stat-delta')?.textContent).toContain('from $42,000 at the start');
    expect(cards[0].querySelector('app-completeness-badge')?.textContent).toContain('Known');
  });

  it('badges the balance with whichever end of the period is worse off', async () => {
    const { cards } = await render({
      ...SAMPLE_PERFORMANCE,
      startingBalance: { ...SAMPLE_PERFORMANCE.startingBalance, isComplete: false, holdingsMissingSnapshot: 2 },
      endingBalance: {
        ...SAMPLE_PERFORMANCE.endingBalance,
        isComplete: false,
        missing: [{ accountId: 'a', accountHoldingId: 'h', symbol: 'ZXP', cause: 'PricesPending' }],
      },
    });

    // An estimate at the start outranks the end merely waiting on prices.
    expect(cards[0].querySelector('app-completeness-badge')?.textContent).toContain('Estimate');
  });

  it('features the money-weighted "Your return" and explains both returns', async () => {
    const { cards } = await render(SAMPLE_PERFORMANCE);

    expect(cards[1].classList).toContain('featured');
    expect(cards[1].querySelector('.stat-value')?.textContent).toContain('+12.1%');
    expect(cards[1].querySelector('.stat-delta')?.textContent).toContain('a year');
    expect(cards[1].querySelector('.stat-hint')?.textContent).toContain('your personal rate of return');
    expect(cards[2].querySelector('.stat-value')?.textContent).toContain('+11.4%');
    expect(cards[2].querySelector('.stat-delta')?.textContent).toContain('over the period');
    expect(cards[2].querySelector('.stat-hint')?.textContent).toContain("fund's published return");
  });

  it('shows gross deposits and withdrawals under net contributions by default (account scope)', async () => {
    const { cards } = await render(SAMPLE_PERFORMANCE);

    expect(cards[3].querySelector('.stat-value')?.textContent).toContain('$9,000');
    expect(cards[3].querySelector('.stat-delta')?.textContent).toContain('$12,000 in · $3,000 out');
  });

  it('shows only net contributions when gross flows are off (portfolio scope)', async () => {
    const { cards } = await render(SAMPLE_PERFORMANCE, { showGrossFlows: false });

    expect(cards[3].querySelector('.stat-value')?.textContent).toContain('$9,000');
    expect(cards[3].querySelector('.stat-delta')).toBeNull();
  });
});
