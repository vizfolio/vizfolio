import { PerformanceReturn } from '../../core/api/models/performance.models';
import { formatReturn, formatSpan, returnFigure } from './performance-format';
import { causeText, fallbackText, missingText, returnMethodText, returnReasonText } from './reason-text';

function ret(overrides: Partial<PerformanceReturn>): PerformanceReturn {
  return {
    rate: 0.1,
    method: 'DailyValuedTWR',
    basis: 'Period',
    reason: null,
    annualizedRate: null,
    periodRate: null,
    fallbackReason: null,
    ...overrides,
  };
}

describe('return reason text', () => {
  it('names how each return was computed', () => {
    expect(returnMethodText(ret({ method: 'XIRR', basis: 'Annualized' }))).toBe('Money-weighted (XIRR)');
    expect(returnMethodText(ret({}))).toBe('Time-weighted, valued daily');
    expect(returnMethodText(ret({ method: 'ModifiedDietz' }))).toBe('Approximate (Modified Dietz)');
  });

  it('explains why a return could not be computed, keeping unknown codes visible', () => {
    expect(returnReasonText('NoInvestedBalance')).toBe('Nothing was invested during this period.');
    expect(returnReasonText('IncompleteEndingBalance')).toContain('end of the period');
    expect(returnReasonText('SomethingNew')).toBe('SomethingNew');
    expect(returnReasonText(null)).toBeNull();
  });

  it('labels a fallback to Modified Dietz as approximate, with the cause', () => {
    expect(fallbackText(ret({ method: 'ModifiedDietz', fallbackReason: 'NoPrice' }))).toBe(
      "Approximate: some days couldn't be valued (no price available).",
    );
    expect(fallbackText(ret({}))).toBeNull();
    expect(causeText('Unmapped')).toContain('valued');
  });
});

describe('returnFigure', () => {
  const FROM = '2019-03-01';
  const TO = '2024-04-01';

  it('shows a multi-year money-weighted return per year, with the total underneath', () => {
    expect(returnFigure(ret({ method: 'XIRR', basis: 'Annualized', rate: 0.12, annualizedRate: 0.12, periodRate: 0.78 }), FROM, TO)).toEqual({
      value: '+12.0%',
      unit: 'a year',
      detail: '+78.0% in total over 5.1 years',
    });
  });

  it('shows a multi-year time-weighted total per year too, so both cards read the same way', () => {
    expect(returnFigure(ret({ rate: 0.21, annualizedRate: 0.1, periodRate: 0.21 }), '2024-01-01', '2026-01-01')).toEqual({
      value: '+10.0%',
      unit: 'a year',
      detail: '+21.0% in total over 2 years',
    });
  });

  it('shows a return under a year as the total, with the span', () => {
    expect(returnFigure(ret({ rate: 0.03, periodRate: 0.03 }), '2026-08-01', '2026-09-01')).toEqual({
      value: '+3.0%',
      unit: 'in total',
      detail: 'over 4 weeks',
    });
  });

  it('reads the older response shape, with only rate and basis', () => {
    expect(returnFigure(ret({ method: 'XIRR', basis: 'Annualized', rate: 0.12 }), FROM, TO).unit).toBe('a year');
    expect(returnFigure(ret({ rate: 0.05 }), '2026-03-01', '2026-09-01').value).toBe('+5.0%');
  });

  it('marks a fallback as approximate', () => {
    expect(returnFigure(ret({ method: 'ModifiedDietz', fallbackReason: 'StalePrice' }), '2026-03-01', '2026-09-01').detail).toBe(
      'over 6 months · approximate',
    );
  });

  it('gives the reason when the return is unknown', () => {
    expect(returnFigure(ret({ rate: null, reason: 'NoSignChange' }), FROM, TO)).toEqual({
      value: '—',
      unit: null,
      detail: 'Not enough deposits or withdrawals to compute your return.',
    });
  });

  it('falls back to "over the period" without dates', () => {
    expect(returnFigure(ret({}), '', '').detail).toBe('over the period');
  });

  it('formats a return as one phrase', () => {
    expect(formatReturn(ret({ method: 'XIRR', basis: 'Annualized', rate: 0.12 }), FROM, TO)).toBe('+12.0% a year');
    expect(formatReturn(ret({ rate: null }), FROM, TO)).toBe('—');
  });
});

describe('formatSpan', () => {
  it('describes how long a period is', () => {
    expect(formatSpan('2019-03-01', '2024-04-01')).toBe('5.1 years');
    expect(formatSpan('2025-01-01', '2026-01-01')).toBe('1 year');
    expect(formatSpan('2026-01-01', '2026-09-01')).toBe('8 months');
    expect(formatSpan('2026-08-01', '2026-08-22')).toBe('3 weeks');
    expect(formatSpan('2026-08-01', '2026-08-02')).toBe('1 day');
    expect(formatSpan(null, '2026-08-02')).toBeNull();
  });
});

describe('missing text', () => {
  it('names what could not be valued and why, with how many more', () => {
    expect(missingText([])).toBeNull();
    expect(missingText(undefined)).toBeNull();
    expect(
      missingText(
        [
          { accountId: 'a1', accountHoldingId: 'h1', symbol: 'ZXFND', cause: 'StalePrice' },
          { accountId: 'a1', accountHoldingId: null, symbol: null, cause: 'NoPrice' },
        ],
        'at the end of the period',
      ),
    ).toBe("We couldn't value ZXFND at the end of the period: no recent price (+1 more).");
    expect(missingText([{ accountId: 'a1', accountHoldingId: null, symbol: null, cause: 'MaterialMismatch' }])).toBe(
      "We couldn't value cash: your history doesn't match your broker's statement.",
    );
  });

  it('explains the remaining return reason codes in plain words', () => {
    expect(returnReasonText('InvalidSubPeriod')).toContain("couldn't be valued");
    expect(returnReasonText('InsufficientIntermediateSnapshots')).toContain('Not enough valuations');
  });
});
