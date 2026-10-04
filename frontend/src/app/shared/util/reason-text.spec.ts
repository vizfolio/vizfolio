import { PerformanceReturn } from '../../core/api/models/performance.models';
import { returnDetail } from './performance-format';
import { causeText, fallbackText, returnMethodText, returnReasonText } from './reason-text';

function ret(overrides: Partial<PerformanceReturn>): PerformanceReturn {
  return {
    rate: 0.1,
    method: 'DailyValuedTWR',
    basis: 'Period',
    reason: null,
    annualizedRate: null,
    fallbackReason: null,
    ...overrides,
  };
}

describe('return reason text', () => {
  it('names how each return was computed, and over what span', () => {
    expect(returnMethodText(ret({ method: 'XIRR', basis: 'Annualized' }))).toBe(
      'Money-weighted (XIRR) · annualized',
    );
    expect(returnMethodText(ret({ method: 'XIRR', basis: 'Period' }))).toBe(
      'Money-weighted (XIRR) · over the period',
    );
    expect(returnMethodText(ret({}))).toBe('Time-weighted, valued daily · over the period');
    expect(returnMethodText(ret({ method: 'ModifiedDietz' }))).toBe(
      'Approximate (Modified Dietz) · over the period',
    );
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

describe('returnDetail', () => {
  it('says a money-weighted figure from a year up is per year', () => {
    expect(returnDetail(ret({ method: 'XIRR', basis: 'Annualized' }))).toBe('a year');
  });

  it('gives the annualized equivalent of a multi-year time-weighted return', () => {
    expect(returnDetail(ret({ rate: 0.21, annualizedRate: 0.1 }))).toBe('+10.0% a year');
  });

  it('says a short-period return covers the period', () => {
    expect(returnDetail(ret({}))).toBe('over the period');
  });

  it('marks a fallback as approximate', () => {
    expect(returnDetail(ret({ method: 'ModifiedDietz', fallbackReason: 'StalePrice' }))).toBe(
      'over the period · approximate',
    );
  });

  it('gives the reason when the return is unknown', () => {
    expect(returnDetail(ret({ rate: null, reason: 'NoSignChange' }))).toBe(
      'Not enough deposits or withdrawals to compute your return.',
    );
  });
});
