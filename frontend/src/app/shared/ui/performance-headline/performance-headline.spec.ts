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
    expect(cards[1].querySelector('.stat-hint')?.textContent).toContain('your personal rate of return');
    expect(cards[2].querySelector('.stat-value')?.textContent).toContain('+11.4%');
    expect(cards[2].querySelector('.stat-hint')?.textContent).toContain("fund's published return");
  });

  it('shows a multi-year return per year, with the total it adds up to underneath, on both cards', async () => {
    const { cards } = await render({
      ...SAMPLE_PERFORMANCE,
      from: '2019-03-01',
      to: '2024-04-01',
      returns: {
        moneyWeighted: { ...SAMPLE_PERFORMANCE.returns.moneyWeighted, rate: 0.12, annualizedRate: 0.12, periodRate: 0.78 },
        timeWeighted: { ...SAMPLE_PERFORMANCE.returns.timeWeighted, rate: 0.7, annualizedRate: 0.11, periodRate: 0.7 },
      },
    });

    for (const [card, perYear, total] of [
      [cards[1], '+12.0%', '+78.0% in total over 5.1 years'],
      [cards[2], '+11.0%', '+70.0% in total over 5.1 years'],
    ] as const) {
      expect(card.querySelector('.stat-value')?.textContent).toContain(perYear);
      expect(card.querySelector('.stat-unit')?.textContent).toContain('a year');
      expect(card.querySelector('.stat-delta')?.textContent).toContain(total);
    }
  });

  it('shows a return under a year as the total, with how long the period is', async () => {
    const { cards } = await render({
      ...SAMPLE_PERFORMANCE,
      from: '2026-03-01',
      to: '2026-09-01',
      returns: {
        moneyWeighted: { ...SAMPLE_PERFORMANCE.returns.moneyWeighted, rate: 0.032, basis: 'Period', annualizedRate: null, periodRate: 0.032 },
        timeWeighted: { ...SAMPLE_PERFORMANCE.returns.timeWeighted, rate: 0.03, annualizedRate: null, periodRate: 0.03 },
      },
    });

    expect(cards[1].querySelector('.stat-value')?.textContent).toContain('+3.2%');
    expect(cards[1].querySelector('.stat-unit')?.textContent).toContain('in total');
    expect(cards[1].querySelector('.stat-delta')?.textContent).toContain('over 6 months');
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
