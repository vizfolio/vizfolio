import { TestBed } from '@angular/core/testing';

import { SAMPLE_PERFORMANCE } from '../../../features/dashboard/dashboard.util';
import { ReturnsChart } from './returns-chart';

async function setup() {
  const fixture = TestBed.createComponent(ReturnsChart);
  fixture.componentRef.setInput('performance', SAMPLE_PERFORMANCE);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  const [percentBtn, gainBtn] = Array.from(el.querySelectorAll<HTMLButtonElement>('.toggle button'));
  return { fixture, el, cmp: fixture.componentInstance as any, percentBtn, gainBtn };
}

describe('ReturnsChart', () => {
  it('is titled "Investment returns over time" and notes the series spacing', async () => {
    const { el } = await setup();

    expect(el.querySelector('h3')?.textContent).toContain('Investment returns over time');
    expect(el.querySelector('.chart-note')?.textContent).toContain('Month-end values');
  });

  it('defaults to the cumulative return in percent', async () => {
    const { cmp, percentBtn, gainBtn } = await setup();

    expect(percentBtn.getAttribute('aria-pressed')).toBe('true');
    expect(gainBtn.getAttribute('aria-pressed')).toBe('false');
    expect(cmp.valueFormat()).toBe('percent');
    expect(cmp.datasets()[0].label).toBe('Cumulative investment return');
    expect(cmp.datasets()[0].data.at(-1)).toBe(11.4);
  });

  it('says the line is time-weighted and ends at the investment return, not the headline', async () => {
    const { el } = await setup();

    expect(el.querySelector('.chart-caption')?.textContent).toContain(
      'Time-weighted: ends at the investment return, not your return.',
    );
  });

  it('flags the line as approximate when the time-weighted return fell back to Modified Dietz', async () => {
    const fixture = TestBed.createComponent(ReturnsChart);
    fixture.componentRef.setInput('performance', {
      ...SAMPLE_PERFORMANCE,
      returns: {
        ...SAMPLE_PERFORMANCE.returns,
        timeWeighted: {
          ...SAMPLE_PERFORMANCE.returns.timeWeighted,
          method: 'ModifiedDietz',
          fallbackReason: 'NoPrice',
        },
      },
    });
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.chart-caption')?.textContent).toContain(
      'Approximate',
    );
  });

  it('switches to the investment gain in the portfolio currency', async () => {
    const { fixture, cmp, percentBtn, gainBtn } = await setup();

    gainBtn.click();
    await fixture.whenStable();

    expect(gainBtn.textContent).toContain('USD');
    expect(gainBtn.getAttribute('aria-pressed')).toBe('true');
    expect(percentBtn.getAttribute('aria-pressed')).toBe('false');
    expect(cmp.valueFormat()).toBe('currency');
    expect(cmp.datasets()[0].label).toBe('Investment gain');
    expect(cmp.datasets()[0].data.at(-1)).toBe(7750);
  });

  it('uses an h2 heading when placed directly on a page', async () => {
    const fixture = TestBed.createComponent(ReturnsChart);
    fixture.componentRef.setInput('performance', SAMPLE_PERFORMANCE);
    fixture.componentRef.setInput('headingLevel', 2);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('h2')?.textContent).toContain(
      'Investment returns over time',
    );
  });
});
