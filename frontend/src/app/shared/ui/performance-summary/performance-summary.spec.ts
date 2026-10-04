import { TestBed } from '@angular/core/testing';

import { PortfolioPerformance } from '../../../core/api/models/performance.models';
import { SAMPLE_PERFORMANCE } from '../../../features/dashboard/dashboard.util';
import { PerformanceSummary } from './performance-summary';

async function render(performance: PortfolioPerformance) {
  const fixture = TestBed.createComponent(PerformanceSummary);
  fixture.componentRef.setInput('performance', performance);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

function withReturns(returns: Partial<PortfolioPerformance['returns']>): PortfolioPerformance {
  return { ...SAMPLE_PERFORMANCE, returns: { ...SAMPLE_PERFORMANCE.returns, ...returns } };
}

describe('PerformanceSummary', () => {
  it('leads with the shared headline row', async () => {
    const el = await render(SAMPLE_PERFORMANCE);

    expect(el.querySelector('app-performance-headline')).toBeTruthy();
    expect(el.querySelector('.detail-card')).toBeNull();
  });

  it('passes the gross-flow setting through to the headline (off at portfolio scope)', async () => {
    const fixture = TestBed.createComponent(PerformanceSummary);
    fixture.componentRef.setInput('performance', SAMPLE_PERFORMANCE);
    fixture.componentRef.setInput('showGrossFlows', false);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('$12,000 in');
  });

  it('keeps the calculation details in a panel that starts closed', async () => {
    const el = await render(SAMPLE_PERFORMANCE);

    const panel = el.querySelector<HTMLDetailsElement>('details.calc');
    expect(panel?.open).toBe(false);
    expect(panel?.querySelector('summary')?.textContent).toContain('How this is calculated');
    expect(panel?.textContent).toContain('Money-weighted (XIRR)');
    expect(panel?.textContent).toContain('Time-weighted, valued daily');
  });

  it('shows the annualized investment return for a period of a year or more', async () => {
    const el = await render(
      withReturns({
        timeWeighted: { ...SAMPLE_PERFORMANCE.returns.timeWeighted, rate: 0.21, annualizedRate: 0.1 },
      }),
    );

    expect(el.textContent).toContain('Investment return, per year');
    expect(el.textContent).toContain('+10.0% a year');
  });

  it('labels a Modified Dietz fallback as approximate and says why', async () => {
    const el = await render(
      withReturns({
        timeWeighted: {
          ...SAMPLE_PERFORMANCE.returns.timeWeighted,
          method: 'ModifiedDietz',
          fallbackReason: 'StalePrice',
        },
      }),
    );

    expect(el.textContent).toContain('Approximate (Modified Dietz)');
    expect(el.textContent).toContain("Approximate: some days couldn't be valued (no recent price).");
  });

  it('explains an unknown return in plain language', async () => {
    const el = await render(
      withReturns({
        moneyWeighted: { ...SAMPLE_PERFORMANCE.returns.moneyWeighted, rate: null, reason: 'NoSignChange' },
      }),
    );

    expect(el.textContent).toContain('Not enough deposits or withdrawals to compute your return.');
  });

  it('stays provider-agnostic', async () => {
    const el = await render({
      ...SAMPLE_PERFORMANCE,
      startingBalance: { ...SAMPLE_PERFORMANCE.startingBalance, value: 0 },
    });

    expect(el.textContent).not.toContain('Vanguard');
  });
});
