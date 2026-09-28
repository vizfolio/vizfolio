import {
  SAMPLE_PERFORMANCE,
  buildReturnSeries,
  buildValueSeries,
  formatCurrency,
  formatPercent,
  trendOf,
} from './dashboard.util';

describe('dashboard.util', () => {
  describe('formatCurrency', () => {
    it('formats whole-dollar amounts', () => {
      expect(formatCurrency(58750, 'USD')).toBe('$58,750');
    });

    it('falls back gracefully for an unknown currency code', () => {
      expect(formatCurrency(100, 'ZZZ')).toContain('100');
    });
  });

  describe('formatPercent', () => {
    it('adds a + sign and % for positive rates', () => {
      expect(formatPercent(0.114)).toBe('+11.4%');
    });

    it('keeps the - sign for negative rates', () => {
      expect(formatPercent(-0.05)).toBe('-5.0%');
    });

    it('shows an em dash when the rate is null', () => {
      expect(formatPercent(null)).toBe('—');
    });
  });

  describe('trendOf', () => {
    it('maps sign to up/down/neutral', () => {
      expect(trendOf(5)).toBe('up');
      expect(trendOf(-5)).toBe('down');
      expect(trendOf(0)).toBe('neutral');
      expect(trendOf(null)).toBe('neutral');
    });
  });

  describe('buildValueSeries', () => {
    it('maps the API series onto aligned arrays that open and close at the reported balances', () => {
      const series = buildValueSeries(SAMPLE_PERFORMANCE);

      expect(series.labels.length).toBe(SAMPLE_PERFORMANCE.series.points.length);
      expect(series.value.length).toBe(series.labels.length);
      expect(series.deposits.length).toBe(series.labels.length);
      expect(series.withdrawals.length).toBe(series.labels.length);

      expect(series.value.at(0)).toBe(SAMPLE_PERFORMANCE.startingBalance.value);
      expect(series.value.at(-1)).toBe(SAMPLE_PERFORMANCE.endingBalance.value);
      expect(series.labels[0]).toBe('Jun 25');
      expect(series.note).toBe('Month-end values');
    });

    it('charts withdrawals as positive magnitudes whose totals match the contributions', () => {
      const series = buildValueSeries(SAMPLE_PERFORMANCE);
      const sum = (xs: number[]) => xs.reduce((a, b) => a + b, 0);

      expect(series.withdrawals.every((w) => w >= 0)).toBe(true);
      expect(sum(series.deposits)).toBe(SAMPLE_PERFORMANCE.contributions.deposits);
      expect(sum(series.withdrawals)).toBe(Math.abs(SAMPLE_PERFORMANCE.contributions.withdrawals));
    });

    it('leaves a gap where a point could not be valued', () => {
      const perf = {
        ...SAMPLE_PERFORMANCE,
        series: {
          interval: 'Monthly' as const,
          points: [
            { date: '2025-01-31', value: 100, deposits: 0, withdrawals: 0, cumulativeReturn: null, investmentGain: null },
            { date: '2025-02-28', value: null, deposits: 0, withdrawals: 0, cumulativeReturn: null, investmentGain: null },
          ],
        },
      };

      expect(buildValueSeries(perf).value).toEqual([100, null]);
    });

    it('labels points to suit the spacing', () => {
      const labelsFor = (interval: 'Weekly' | 'Quarterly') =>
        buildValueSeries({
          ...SAMPLE_PERFORMANCE,
          series: { interval, points: [{ date: '2025-08-31', value: 1, deposits: 0, withdrawals: 0, cumulativeReturn: null, investmentGain: null }] },
        }).labels[0];

      expect(labelsFor('Weekly')).toBe('Aug 31');
      expect(labelsFor('Quarterly')).toBe('Q3 25');
    });
  });

  describe('buildReturnSeries', () => {
    it('opens at zero and ends at the headline time-weighted return and period investment gain', () => {
      const series = buildReturnSeries(SAMPLE_PERFORMANCE);
      const p = SAMPLE_PERFORMANCE;

      expect(series.labels.length).toBe(p.series.points.length);
      expect(series.returnPct.length).toBe(series.labels.length);
      expect(series.gain.length).toBe(series.labels.length);
      expect(series.returnPct[0]).toBe(0);
      expect(series.gain[0]).toBe(0);
      expect(series.returnPct.at(-1)).toBe(11.4);
      expect(series.gain.at(-1)).toBe(
        p.endingBalance.value - p.startingBalance.value - p.contributions.net,
      );
      expect(series.note).toBe('Month-end values');
    });

    it('leaves gaps where the return or gain could not be computed', () => {
      const perf = {
        ...SAMPLE_PERFORMANCE,
        series: {
          interval: 'Monthly' as const,
          points: [
            { date: '2025-01-31', value: 100, deposits: 0, withdrawals: 0, cumulativeReturn: 0.01234, investmentGain: 1.4 },
            { date: '2025-02-28', value: null, deposits: 0, withdrawals: 0, cumulativeReturn: null, investmentGain: null },
          ],
        },
      };

      const series = buildReturnSeries(perf);
      expect(series.returnPct).toEqual([1.23, null]);
      expect(series.gain).toEqual([1, null]);
    });
  });
});
